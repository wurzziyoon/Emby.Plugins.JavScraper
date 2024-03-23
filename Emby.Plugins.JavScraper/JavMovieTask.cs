using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.JavScraper.Scrapers;
using Emby.Plugins.JavScraper.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Serialization;
using System.Reflection;
using MediaBrowser.Controller.Entities.Movies;
using System.IO;

#if __JELLYFIN__
using Microsoft.Extensions.Logging;
#else
using MediaBrowser.Model.Logging;
#endif

namespace Emby.Plugins.JavScraper
{
    public class JavMovieTask : IScheduledTask
    {
        private readonly ILibraryManager libraryManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ImageProxyService imageProxyService;
        private readonly IProviderManager providerManager;
        private readonly IFileSystem fileSystem;
        private readonly ILogger _logger;

        public Gfriends Gfriends { get; }

        public JavMovieTask(
#if __JELLYFIN__
            ILoggerFactory logManager,
#else
            ILogManager logManager,
            ImageProxyService imageProxyService,
            Gfriends gfriends,
#endif
            ILibraryManager libraryManager,
            IJsonSerializer _jsonSerializer, IApplicationPaths appPaths,

            IProviderManager providerManager,
            IFileSystem fileSystem)
        {
            _logger = logManager.CreateLogger<JavPersonTask>();
            this.libraryManager = libraryManager;
            this._jsonSerializer = _jsonSerializer;
#if __JELLYFIN__
            imageProxyService = Plugin.Instance.ImageProxyService;
            Gfriends = new Gfriends(logManager, _jsonSerializer);
#else
            this.imageProxyService = imageProxyService;
            Gfriends = gfriends;
#endif
            this.providerManager = providerManager;
            this.fileSystem = fileSystem;
        }

        public string Name => Plugin.NAME + ": 采集缺失作品封面和信息";
        public string Key => Plugin.NAME + "-Actress";
        public string Description => "采集缺失作品封面和信息,解决本身存在nfo文件时刮削的问题.";
        public string Category => "JavScraper";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            var t = new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerWeekly,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks,
                MaxRuntimeTicks = TimeSpan.FromHours(3).Ticks,
                DayOfWeek = DayOfWeek.Monday
            };
            return new[] { t };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info($"Running...");
            progress.Report(0);

            IDirectoryService ds = default;

            var dstype = typeof(DirectoryService);
            var cr = dstype.GetConstructors().Where(o => o.IsPublic && o.IsStatic == false).OrderByDescending(o => o.GetParameters().Length).FirstOrDefault();
            if (cr.GetParameters().Length == 1)
                ds = cr.Invoke(new[] { fileSystem }) as IDirectoryService;
            else
                ds = cr.Invoke(new object[] { _logger, fileSystem }) as IDirectoryService;

            var query = new InternalItemsQuery()
            {
                IncludeItemTypes = new[] { nameof(Movie) }
            };
            //获取所有作品
            List<BaseItem> movies = libraryManager.GetItemList(query)?.ToList();
            _logger.Debug("oMovieSize:" + movies.Count);
            movies = movies.Where(t => t.ImageInfos.Where(v => (!string.IsNullOrEmpty(v.Path) && v.Type == ImageType.Backdrop && !File.Exists(v.Path)) ).Count() > 0 || t.ImageInfos.Count()==0).ToList();
            _logger.Debug("MovieSize:" + movies.Count);


            

            if (movies?.Any() != true)
            {
                progress.Report(100);
                return;
            }
            movies.RemoveAll(o => !(o is Movie));

            for (int i = 0; i < movies.Count; ++i)
            {
                var movie = movies[i];
                _logger.Debug("Executing: ["+ movie.Name + "]" + string.Join(';', movie.ImageInfos.Select(t => t.Type.ToString() + t.Path)));

                var options = new MetadataRefreshOptions(ds)
                {
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh
                };

                try
                {
                    await movie.RefreshMetadata(options, cancellationToken);
                }
                catch { }
                progress.Report(i * 1.0 / movies.Count * 100);
            }

            progress.Report(100);
        }
    }
}