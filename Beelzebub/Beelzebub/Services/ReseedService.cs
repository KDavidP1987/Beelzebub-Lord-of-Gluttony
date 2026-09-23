using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beelzebub.Logic;

namespace Beelzebub.Services;

/// <summary>
/// v0.135.0 — `.beelz admin reseed`: bring a live server's ability_rules.json up to the defaults shipped in THIS
/// build without wiping the admin's own curation (the recurring "new default isn't live on my server" gap).
///
///   preview  — dry-run the merge and report what would change (nothing is written).
///   merge    — 3-way merge (<see cref="RulesMerge"/>): shipped changes land where the admin never changed the old
///              default; admin edits are kept; both-changed = conflict, admin wins. Without a valid baseline
///              (older servers) it runs in SAFE mode: add missing entries + adopt shipped Enabled=false only.
///   replace  — overwrite with the shipped default (admin curation is lost; a backup is kept).
///
/// Every write: timestamped backup → write temp → atomic replace → baseline advanced → reload. On failure the
/// backup is restored.
/// </summary>
internal static class ReseedService
{
    public sealed class Outcome
    {
        public bool Ok;
        public string Message = "";
        public MergeReport Report;
        public string BackupPath;
    }

    /// <summary>Run the merge in memory. Never writes.</summary>
    public static Outcome Plan()
    {
        var o = new Outcome();
        var rules = Core.AbilityRules;
        string shipRaw = AbilityRules.EmbeddedDefaultJson();
        if (shipRaw == null) { o.Message = "the shipped default isn't embedded in this build — nothing to reseed from."; return o; }
        if (!File.Exists(rules.RulesFilePath)) { o.Message = "no ability_rules.json yet — it's seeded from the shipped default on the next start."; return o; }

        JsonObject ship, server, baseline = null;
        try
        {
            ship = Parse(AbilityRules.NormalizedJson(shipRaw));
            string serverRaw = File.ReadAllText(rules.RulesFilePath);
            server = Parse(AbilityRules.NormalizedJson(serverRaw));
            string serverHash = (JsonNode.Parse(serverRaw) as JsonObject)?[RulesMerge.BaselineHashKey]?.GetValue<string>();

            if (File.Exists(rules.BaselineFilePath) && !string.IsNullOrEmpty(serverHash))
            {
                string baseNorm = AbilityRules.NormalizedJson(File.ReadAllText(rules.BaselineFilePath));
                if (string.Equals(AbilityRules.HashOf(baseNorm), serverHash, StringComparison.OrdinalIgnoreCase))
                    baseline = Parse(baseNorm);
                else
                    Core.Log.LogWarning("[Beelz RESEED] baseline file doesn't match the rules file's BaselineHash — using SAFE mode.");
            }
        }
        catch (Exception ex) { o.Message = $"couldn't read the rules files: {ex.Message}"; return o; }

        o.Report = RulesMerge.Merge(baseline, server, ship, key => AbilityRules.ResolveAbilityGuid(key));
        if (o.Report.Refused) { o.Message = "merge refused: " + o.Report.RefuseReason; return o; }
        o.Ok = true;
        return o;
    }

    public static Outcome Merge()
    {
        var o = Plan();
        if (!o.Ok) return o;
        if (!o.Report.HasChanges && !o.Report.SafeMode)
        {
            o.Message = "already up to date with the shipped defaults — nothing written.";
            return o;
        }
        return Write(o, o.Report.Merged);
    }

    public static Outcome Replace()
    {
        var o = new Outcome();
        string shipRaw = AbilityRules.EmbeddedDefaultJson();
        if (shipRaw == null) { o.Message = "the shipped default isn't embedded in this build."; return o; }
        try { return Write(o, Parse(AbilityRules.NormalizedJson(shipRaw))); }
        catch (Exception ex) { o.Message = $"couldn't read the shipped default: {ex.Message}"; return o; }
    }

    static Outcome Write(Outcome o, JsonObject content)
    {
        var rules = Core.AbilityRules;
        string path = rules.RulesFilePath;
        string basePath = rules.BaselineFilePath;
        string backup = null, baseBackup = null;
        bool committed = false;
        try
        {
            string shipNorm = AbilityRules.NormalizedJson(AbilityRules.EmbeddedDefaultJson());
            string shipHash = AbilityRules.HashOf(shipNorm);

            var toWrite = (JsonObject)JsonNode.Parse(content.ToJsonString());
            toWrite[RulesMerge.BaselineHashKey] = shipHash;
            string json = toWrite.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            AbilityRules.ValidateCandidate(json);   // must load exactly as Load() would

            backup = $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (File.Exists(path)) File.Copy(path, backup, overwrite: false);
            else backup = null;

            if (File.Exists(basePath))
            {
                baseBackup = backup != null ? backup + ".baseline" : $"{basePath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(basePath, baseBackup, overwrite: true);
            }

            // Stage BOTH files next to their targets, then commit (rules first, then baseline).
            string tmp = path + ".tmp", baseTmp = basePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.WriteAllText(baseTmp, shipNorm);
            committed = true;   // from here a failure must restore both files
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            if (File.Exists(basePath)) File.Replace(baseTmp, basePath, null); else File.Move(baseTmp, basePath);
        }
        catch (Exception ex)
        {
            if (committed)
            {
                try { if (backup != null) File.Copy(backup, path, overwrite: true); } catch { }
                try
                {
                    if (baseBackup != null) File.Copy(baseBackup, basePath, overwrite: true);
                    else if (File.Exists(basePath)) File.Delete(basePath);   // no prior baseline → SAFE mode, as before
                }
                catch { }
            }
            o.Ok = false;
            o.Message = $"write failed ({ex.Message}) — your previous rules file was kept{(backup != null ? " (backup restored)" : "")}.";
            Core.Log.LogError($"[Beelz RESEED] write failed: {ex}");
            return o;
        }

        o.Ok = true;
        o.BackupPath = backup;
        o.Message = backup != null ? $"written. Backup: {Path.GetFileName(backup)}" : "written.";
        // Post-commit refresh: the files are good; a failure here is reported, not rolled back.
        try { AfterWrite(); }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz RESEED] files written but the refresh failed: {ex}");
            o.Message += $" The live refresh failed ({ex.Message}) — run .beelz admin reload.";
        }
        return o;
    }

    /// <summary>Same refresh as `.beelz admin reload`, plus re-resolving every online bar (locks may have changed).</summary>
    public static void AfterWrite()
    {
        Core.AbilityRules.Load();
        BestiaryService.InvalidateTotals();
        CastHistoryService.Invalidate();
        ExclusionService.Invalidate();
        AbilityTuningService.ReapplyAll();
        ExclusionService.ReapplyAllOnline();
    }

    static JsonObject Parse(string json) => JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("not a JSON object");

    /// <summary>Short human lines for chat (first few items of each bucket).</summary>
    public static List<string> Describe(MergeReport r, int perBucket = 6)
    {
        var lines = new List<string> { r.Summary() };
        void Bucket(string title, List<string> items)
        {
            if (items.Count == 0) return;
            lines.Add($"{title} ({items.Count}): {string.Join(", ", items.GetRange(0, Math.Min(perBucket, items.Count)))}{(items.Count > perBucket ? ", …" : "")}");
        }
        Bucket("added", r.Added);
        Bucket("adopted", r.Adopted);
        Bucket("removed", r.Removed);
        Bucket("conflicts (your value kept)", r.Conflicts);
        return lines;
    }
}
