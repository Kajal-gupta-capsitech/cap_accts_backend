using BackendAcctTask.Models;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Text;

namespace BackendAcctTask.Utils;

public static class CsvUtils
{
    public static async Task<List<ImportRow>> ParseTrialBalanceCsv(IFormFile file)
    {
        var rows = new List<ImportRow>();

        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);

        // Skip header
        var headerLine = await reader.ReadLineAsync();

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return rows;
        }

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);

            if (values.Count < 4)
            {
                continue;
            }

            var code = values[0].Trim();
            var name = values[1].Trim();

            decimal debit = 0;
            decimal credit = 0;

            decimal.TryParse(
                values[2],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out debit
            );

            decimal.TryParse(
                values[3],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out credit
            );

            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var nature = debit > 0
                ? AccountNature.Debit
                : AccountNature.Credit;

            rows.Add(
                new ImportRow
                {
                    Code = code,
                    Name = name,
                    Nature = nature,
                    Debit = debit,
                    Credit = credit,
                    Note = string.Empty
                }
            );
        }

        return rows;
    }

    public static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                // Escaped quote ""
                if (insideQuotes &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }

                continue;
            }

            if (c == ',' && !insideQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString().Trim());

        return values;
    }
}
