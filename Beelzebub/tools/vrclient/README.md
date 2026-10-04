# vrclient — in-game self-tests

Drives the V Rising client to join the local dev server, run chat/console commands, press keys, cast abilities, and
check the results from the server log, DevChatEcho reply lines, and on-screen OCR.

```bash
pip install --user pyautogui pydirectinput winocr pillow pygetwindow
python vrclient.py ensure
python vrclient.py run scenarios/cast_basic.vrs
```

The full method, scenario grammar, limits and porting guide: `Beelzebub/Beelzebub/docs/INGAME_SELF_TEST.md`.
