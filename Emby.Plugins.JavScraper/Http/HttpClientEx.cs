using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugins.JavScraper.Http
{
    /// <summary>
    /// HttpClient
    /// </summary>
    public class HttpClientEx
    {
        /// <summary>
        /// 客户端初始话方法
        /// </summary>
        private readonly Action<HttpClient> ac;

        /// <summary>
        /// 当前客户端
        /// </summary>
        private HttpClient client = null;

        /// <summary>
        /// 配置版本号
        /// </summary>
        private long version = -1;

        /// <summary>
        /// 上一个客户端
        /// </summary>
        private HttpClient client_old = null;

        public HttpClientEx(Action<HttpClient> ac = null)
        {
            this.ac = ac;
            pythonPath = @"C:\ProgramData\miniconda3\python.exe"; // 或 "python3"，或完整路径如 @"C:\Python39\python.exe"
            scriptPath = Path.Combine(@"C:\Users\Administrator\source\repos\javdbScraper", "fetch_url.py");
        }

        /// <summary>
        /// 获取一个 HttpClient
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public HttpClient GetClient()
        {
            if (client != null && version == Plugin.Instance.Configuration.ConfigurationVersion)
                return client;

            if (client_old != null)
            {
                client_old.Dispose();
                client_old = null;
            }
            client_old = client;

            var handler = new ProxyHttpClientHandler();
            client = new HttpClient(handler, true);
            ac?.Invoke(client);

            return client;
        }

        public Task<string> GetStringAsync(string requestUri)
            => ShouldUsePythonScript(requestUri)? GetStringViaPythonAsync(requestUri): GetClient().GetStringAsync(requestUri);

        public Task<HttpResponseMessage> GetAsync(string requestUri)
            => GetClient().GetAsync(requestUri);

        public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken)
            => GetClient().GetAsync(requestUri, cancellationToken);

        public Task<Stream> GetStreamAsync(string requestUri)
            => GetClient().GetStreamAsync(requestUri);

        public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
            => GetClient().PostAsync(requestUri, content);

        public Uri BaseAddress => GetClient().BaseAddress;


        /// <summary>
        /// 判断是否应该使用 Python 脚本
        /// </summary>
        private bool ShouldUsePythonScript(string url)
        {
            List<string> requestPath = new List<string>() { "/search?q=" , "javdb.com/v/", "/search?query=" };
            if (requestPath.Find(t => url.ToLower().Contains(t)).Length > 0) {
                return true;
            }
            return false;
            
        }

        /// <summary>
        /// Python 解释器路径
        /// </summary>
        private readonly string pythonPath;

        /// <summary>
        /// Python 脚本路径
        /// </summary>
        private readonly string scriptPath;

        /// <summary>
        /// 通过 Python 脚本获取 URL 内容
        /// </summary>
        /// <param name="url">要获取的 URL</param>
        /// <returns>响应内容</returns>
        private async Task<string> GetStringViaPythonAsync(string url)
        {
            

            try
            {
                if (!url.StartsWith("https://javdb.com")) {
                    url = "https://javdb.com"+url;
                }
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\" \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                using var process = new Process();
                process.StartInfo = processStartInfo;

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        outputBuilder.AppendLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        errorBuilder.AppendLine(args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    var result = outputBuilder.ToString().Trim();
                    return result;
                }
                else
                {
                    var errorMessage = $"Python script failed with exit code {process.ExitCode}: {errorBuilder}";
                    throw new HttpRequestException(errorMessage);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to execute Python script: {ex.Message}";
                Console.WriteLine(ex.Message);
                throw new HttpRequestException(errorMessage, ex);
            }
        }
    }
}