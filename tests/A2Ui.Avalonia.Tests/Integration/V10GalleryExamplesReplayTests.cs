using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Integration;

public sealed class V10GalleryExamplesReplayTests
{
    public static IEnumerable<object[]> GetV10ExampleFiles()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Specs", "v1_0", "basic");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*.json").Order())
        {
            yield return [Path.GetFileName(file)];
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(GetV10ExampleFiles))]
    public void V10Example_ReplaysSuccessfully(string filename)
    {
        RenderResult result = GalleryTestHelper.ReplayV10Example($"v1_0/basic/{filename}");

        Assert.NotNull(result);
        Assert.NotNull(result.RootControl);
        Assert.NotNull(result.Surface);
        Assert.NotEmpty(result.Surface.Components);
        Assert.Empty(result.WarningsAndErrors ?? []);
    }

    [AvaloniaFact]
    public void Example_01_FlightStatus_V10_RendersCorrectStructure()
    {
        RenderResult result = GalleryTestHelper.ReplayV10Example("v1_0/basic/01_flight-status.json");

        Assert.Empty(result.WarningsAndErrors ?? []);

        // Root is a Card -> Border
        Assert.IsType<Border>(result.RootControl);

        List<TextBlock> texts = GalleryTestHelper.FindAll<TextBlock>(result.RootControl);
        Assert.Contains(texts, tb => tb.Text == "OS 87");
        Assert.Contains(texts, tb => tb.Text == "Mon, Dec 15");
        Assert.Contains(texts, tb => tb.Text?.Contains("Vienna") == true);
        Assert.Contains(texts, tb => tb.Text?.Contains("New York") == true);
        Assert.Contains(texts, tb => tb.Text == "On Time");
        Assert.Contains(texts, tb => tb.Text == "Departs");
        Assert.Contains(texts, tb => tb.Text == "10:15 AM");
        Assert.Contains(texts, tb => tb.Text == "Arrives");
        Assert.Contains(texts, tb => tb.Text == "2:30 PM");
    }

    [AvaloniaFact]
    public void Example_09_LoginForm_V10_RendersCorrectStructure()
    {
        RenderResult result = GalleryTestHelper.ReplayV10Example("v1_0/basic/09_login-form.json");

        List<TextBox> textBoxes = GalleryTestHelper.FindAll<TextBox>(result.RootControl);
        Assert.NotEmpty(textBoxes);

        List<Button> buttons = GalleryTestHelper.FindAll<Button>(result.RootControl);
        Assert.NotEmpty(buttons);
    }
}
