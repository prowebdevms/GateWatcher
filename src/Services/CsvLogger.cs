using System.Text;

namespace GateWatcher.Services
{
    public sealed class CsvLogger
    {
        private readonly string _dir;
        public CsvLogger(string? dir = null)
        {
            _dir = dir ?? AppContext.BaseDirectory;
            try { Directory.CreateDirectory(_dir); } catch { }
        }
        private string PathFor(DateTime localNow) => System.IO.Path.Combine(_dir, $"stats_{localNow:yyyy-MM-dd}.csv");

        public void AppendMany(IEnumerable<string[]> rows)
        {
            var now = DateTime.Now;
            var path = PathFor(now);
            var newFile = !File.Exists(path);
            using var sw = new StreamWriter(path, append: true, Encoding.UTF8);
            if (newFile) sw.WriteLine("time,type,rank,pair,last,quote_volume,change_percentage");
            foreach (var r in rows)
            {
                string Esc(string s) => s.Contains(',') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
                sw.WriteLine(string.Join(",", r.Select(Esc)));
            }
        }
    }
}
