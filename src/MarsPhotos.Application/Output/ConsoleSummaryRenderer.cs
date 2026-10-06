using System.Text;
using MarsPhotos.Domain;

namespace MarsPhotos.Application.Output;

/// <summary>Renders a <see cref="RunSummary"/> as a plain-text table with totals (FR-019, FR-021, AC-7).</summary>
public static class ConsoleSummaryRenderer
{
    private const int MaxInputWidth = 32;
    private static readonly string[] Headers = ["Input", "Date", "Status", "Source", "New", "Skip", "Fail", "Notes"];

    public static string Render(RunSummary summary)
    {
        var rows = summary.Results.Select(Row).ToList();
        var widths = Headers.Select(h => h.Length).ToArray();
        foreach (var row in rows)
        {
            for (var c = 0; c < 7; c++)
            {
                widths[c] = Math.Max(widths[c], row[c].Length);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Mars Rover Photo Downloader - rover: {summary.Rover}");
        sb.AppendLine(Format(Headers, widths));
        foreach (var row in rows)
        {
            sb.AppendLine(Format(row, widths));
        }

        var t = summary.Totals;
        sb.AppendLine(
            $"Totals: {t.Lines} lines | {t.Valid} valid | {t.Invalid} invalid | {t.OutsideMissionRange} outside range | "
            + $"{t.Downloaded} downloaded | {t.Skipped} skipped | {t.Failed} failed");
        return sb.ToString();
    }

    private static string[] Row(DateResult r)
    {
        var notes = new List<string>();
        notes.AddRange(r.Errors.Select(e => Redaction.RedactApiKey($"{e.Code}: {e.Message}")));
        if (!string.IsNullOrWhiteSpace(r.Note))
        {
            notes.Add(Redaction.RedactApiKey(r.Note));
        }

        if (!string.IsNullOrWhiteSpace(r.FallbackReason))
        {
            notes.Add(Redaction.RedactApiKey($"fallback: {r.FallbackReason}"));
        }

        return
        [
            Truncate(Redaction.RedactApiKey(r.Input), MaxInputWidth),
            r.Date?.ToString("yyyy-MM-dd") ?? "-",
            r.Status.ToString(),
            r.Provider ?? "-",
            r.Downloaded.ToString(),
            r.Skipped.ToString(),
            r.Failed.ToString(),
            string.Join(" | ", notes),
        ];
    }

    private static string Format(string[] cells, int[] widths)
    {
        var sb = new StringBuilder();
        for (var c = 0; c < 7; c++)
        {
            var numeric = c is 4 or 5 or 6;
            sb.Append(numeric ? cells[c].PadLeft(widths[c]) : cells[c].PadRight(widths[c]));
            sb.Append("  ");
        }

        sb.Append(cells[7]);
        return sb.ToString().TrimEnd();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
