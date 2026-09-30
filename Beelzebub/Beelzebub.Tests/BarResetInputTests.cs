using Beelzebub.Logic;
using Xunit;

// bar-reset D25 — only CONFIRM (trimmed, case-insensitive) confirms a reset.
public class BarResetInputTests
{
    [Theory]
    [InlineData("CONFIRM")]
    [InlineData("confirm")]
    [InlineData(" CONFIRM ")]
    [InlineData(" confirm ")]
    public void IsConfirm_fails_when_a_valid_token_is_rejected(string token) => Assert.True(BarResetInput.IsConfirm(token));

    [Theory]
    [InlineData("CONFIRMED")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("yes")]
    [InlineData("CON FIRM")]
    public void IsConfirm_fails_when_another_token_is_accepted(string token) => Assert.False(BarResetInput.IsConfirm(token));
}
