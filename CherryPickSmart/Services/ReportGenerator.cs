using CherryPickSmart.Models;

public class ReportGenerator(string templatePath)
{
    public void Generate(AnalysisResult analysis, string outputPath)
    {
        var templateText = File.ReadAllText(templatePath);
        var template = Scriban.Template.Parse(templateText);
        if (template.HasErrors)
            throw new InvalidOperationException(
                "Template errors:\n" + string.Join("\n", template.Messages));

        // Render the template with a model named "analysis"
        var html = template.Render(new { analysis });
        File.WriteAllText(outputPath, html);
    }
}