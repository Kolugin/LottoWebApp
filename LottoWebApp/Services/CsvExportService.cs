using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public static class CsvExportService
    {
        // Папка для выгрузок внутри wwwroot
        private static readonly string outputDirectory =
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "exports");

        public static async Task<string> ExportToCsvAsync(DataTable table, string fileName)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            const int rowLimit = 500000;
            const int maxColumnsPerFile = 15000;

            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            int totalColumns = table.Columns.Count;
            int fileIndex = 1;
            int rowCount = 0;
            StreamWriter writer = null;

            try
            {
                for (int columnStart = 0; columnStart < totalColumns; columnStart += maxColumnsPerFile)
                {
                    int columnEnd = Math.Min(columnStart + maxColumnsPerFile, totalColumns);
                    string currentFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{fileIndex}{Path.GetExtension(fileName)}";
                    string currentFilePath = Path.Combine(outputDirectory, currentFileName);

                    writer = new StreamWriter(currentFilePath, false, new UTF8Encoding(true));
                    rowCount = 0;

                    // Заголовки
                    for (int i = columnStart; i < columnEnd; i++)
                    {
                        await writer.WriteAsync(table.Columns[i].ColumnName);
                        if (i < columnEnd - 1)
                            await writer.WriteAsync(";");
                    }
                    await writer.WriteLineAsync();

                    // Строки
                    foreach (DataRow row in table.Rows)
                    {
                        if (rowCount >= rowLimit)
                        {
                            await writer.DisposeAsync();
                            fileIndex++;
                            rowCount = 0;

                            currentFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{fileIndex}{Path.GetExtension(fileName)}";
                            currentFilePath = Path.Combine(outputDirectory, currentFileName);
                            writer = new StreamWriter(currentFilePath, false, new UTF8Encoding(true));

                            // Заголовки повторяем
                            for (int i = columnStart; i < columnEnd; i++)
                            {
                                await writer.WriteAsync(table.Columns[i].ColumnName);
                                if (i < columnEnd - 1)
                                    await writer.WriteAsync(";");
                            }
                            await writer.WriteLineAsync();
                        }

                        for (int i = columnStart; i < columnEnd; i++)
                        {
                            await writer.WriteAsync(row[i]?.ToString() ?? "");
                            if (i < columnEnd - 1)
                                await writer.WriteAsync(";");
                        }
                        await writer.WriteLineAsync();
                        rowCount++;
                    }

                    await writer.DisposeAsync();
                    fileIndex++;
                }
            }
            finally
            {
                if (writer != null)
                    await writer.DisposeAsync();

                stopwatch.Stop();
            }

            // Возвращаем путь вида /exports/filename.csv (для <a download>)
            return $"/exports/{Path.GetFileNameWithoutExtension(fileName)}_1{Path.GetExtension(fileName)}";
        }
    }
}
