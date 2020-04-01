using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TBYahooFinanceDownloader
{
    class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private const int waitBetweenAPIcallsToAvoidThrottingInMilliSeconds = 500;
        private const string tickersFileName = "tickers.txt";

        static async Task Main(string[] args)
        {
            string[] tickers;

            if (!File.Exists(tickersFileName)) using (StreamWriter w = File.AppendText(tickersFileName)) {
                w.Write("MSFT\nAAPL\nPM\bPG"); // sample tickers
            }

            try
            {   // Open the text file using a stream reader.
                using (StreamReader sr = new StreamReader(tickersFileName))
                {
                    // Read the stream to a string, and write the string to the console.
                    String tickersFile = sr.ReadToEnd();

                    tickers = tickersFile
                        .Replace('\n', ',')
                        .Replace('\r', ',')
                        .Split(',')
                        .Where(rawTicker => !String.IsNullOrWhiteSpace(rawTicker))
                        .Select(rawTicker => new String(rawTicker.Where(Char.IsLetter).ToArray()).ToUpper())
                        .Distinct()
                        .ToArray();

                    Console.WriteLine($"Tickers: {String.Join(", ", tickers)}\n");
                    Console.WriteLine($"Note 1: Edit tickers.txt file to request different stocks.");
                    Console.WriteLine($"Note 2: App will wait {waitBetweenAPIcallsToAvoidThrottingInMilliSeconds} milliseconds between each ticker query to avoid throtting.\n");
                }
            }
            catch (IOException e)
            {
                Console.WriteLine("Error: tickers.txt could not be read or not in valid format.");
                Console.WriteLine(e.Message);
                Console.WriteLine("Press any key to exit");
                Console.ReadKey();
                return;
            }

            Dictionary<string, Dictionary<string, decimal>> tickersHistory = new Dictionary<string, Dictionary<string, decimal>>();

            foreach (var ticker in tickers) {
                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?interval=1wk&range=1y";
                var streamTask = client.GetStreamAsync(url);

                Console.WriteLine($"Request ticker: {ticker}, url: {url}");
                Console.WriteLine($"{ticker} url: {url}");
                var json = (JsonElement) await JsonSerializer.DeserializeAsync<dynamic>(await streamTask);
                Console.WriteLine($"Received ticker: {ticker}\n");

                Thread.Sleep(waitBetweenAPIcallsToAvoidThrottingInMilliSeconds);
                
                var resultJson = json.GetProperty("chart").GetProperty("result")[0];

                var timestampsJson = resultJson.GetProperty("timestamp").EnumerateArray().ToArray();
                var timestamps = timestampsJson.Select(e => e.GetInt64()).ToArray();
                var timestampDateStrings = timestamps.Select(e => DateTimeOffset.FromUnixTimeSeconds(e).Date.ToString("yyyy-MM-dd")).ToArray();

                var closesJson = resultJson.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close").EnumerateArray().ToArray();
                var closes = closesJson.Select(e => e.GetDecimal()).ToArray();

                if (timestampDateStrings.Count() != closes.Count())
                    continue;

                Dictionary<string, decimal> closeTimeSeries =
                    Enumerable.Range(0, timestampDateStrings.Count() - 1) // ignore last item as it is generally a duplicate when casted to Date from DateTime
                    .ToArray()
                    .Select(i => new KeyValuePair<string, decimal>(timestampDateStrings[i], closes[i]))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                tickersHistory[ticker] = closeTimeSeries;
            }

            var globalTimestampDateStrings = tickersHistory.SelectMany(s => s.Value.Select(e => e.Key)).Distinct().OrderBy(e => e).ToArray();

            var csvHeaderLine = "," + String.Join(',', globalTimestampDateStrings.Select(e => $"\"{e}\"")) + "\n";
            var csvDataLines = String.Join("\n",
                tickersHistory.Select((kvp) =>
                    kvp.Key + "," +
                    String.Join(',',
                        globalTimestampDateStrings.Select(date => tickersHistory[kvp.Key].GetValueOrDefault(date))
                    )
                )
            );

            string fileName = $"TBYahooFinanceStockHistory-{DateTime.Now.ToString("yyyyMMddHHmmss")}.csv";

            using (StreamWriter file = new StreamWriter(fileName))
            {
                file.WriteLine(csvHeaderLine + csvDataLines);
            }

            Console.WriteLine($"File exported successfully: {fileName}");
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }
    }

    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IDictionary<TKey, TValue> dict, TKey key)
        {
            TValue val;
            if (dict.TryGetValue(key, out val))
                return val;
            return default;
        }
    }
}
