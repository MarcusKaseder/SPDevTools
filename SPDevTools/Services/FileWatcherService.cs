namespace SPDevTools
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.SharePoint;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using SPDevTools.EventListener;

    public class FileWatcherService
    {
        public static FileWatcherService Current { get; private set; }

        private readonly object updateLock = new object();

        public ISharePointProjectService ProjectService { get; }

        private Dictionary<string, FileEventListener> FileListener { get; }

        private FileWatcherService(ISharePointProjectService projectService)
        {
            this.ProjectService = projectService;
            this.FileListener = new Dictionary<string, FileEventListener>(StringComparer.OrdinalIgnoreCase);
        }

        public static void Initialize(ISharePointProjectService projectService)
        {
            var fileWatcherService = new FileWatcherService(projectService);

            var existingProjectItemFiles = projectService.Projects
                .SelectMany(p => p.ProjectItems)
                .SelectMany(i => i.Files)
                .Where(f => f.DeploymentType == DeploymentType.TemplateFile || f.DeploymentType == DeploymentType.RootFile)
                .ToArray();

            foreach (var file in existingProjectItemFiles)
            {
                fileWatcherService.AddFile(file.FullPath);
            }

            FileWatcherService.Current = fileWatcherService;
        }

        public void AddFile(string fileFullPath)
        {
            lock (this.updateLock)
            {
                if (!this.FileListener.ContainsKey(fileFullPath))
                {
                    var service = this.ProjectService.ServiceProvider.GetService(typeof(SVsFileChangeEx)) as IVsFileChangeEx;
                    var fileWatcher = new FileEventListener(fileFullPath, service, this.OnDocumentChanged);

                    this.FileListener.Add(fileFullPath, fileWatcher);
                }
            }
        }

        public void RemoveFile(string fileFullPath)
        {
            lock (this.updateLock)
            {
                if (this.FileListener.TryGetValue(fileFullPath, out var fileWatcher))
                {
                    fileWatcher.Unadvice();

                    this.FileListener.Remove(fileFullPath);
                }
            }
        }

        private void OnDocumentChanged(FileEventListener fileWatcher)
        {
            var projectItemFile = this.GetProjectItemFile(fileWatcher.FileFullPath);

            if (projectItemFile == null)
            {
                return;
            }

            if (projectItemFile.DeploymentType != DeploymentType.RootFile && projectItemFile.DeploymentType != DeploymentType.TemplateFile)
            {
                // Not supported type for copy
                this.RemoveFile(fileWatcher.FileFullPath);

                return;
            }

            var sourcePath = projectItemFile.FullPath;
            var deploymentPath = Path.Combine(projectItemFile.DeploymentRoot, projectItemFile.RelativePath);

            try
            {
                this.ProjectService.Logger.WriteLine($"Copy {projectItemFile.RelativePath} -> {deploymentPath}", LogCategory.Status);

                deploymentPath = this.ReplacePathTokens(projectItemFile.Project, deploymentPath);

                File.Copy(sourcePath, deploymentPath, true);
            }
            catch (Exception ex)
            {
                this.ProjectService.Logger.WriteLine($"Error: {ex.Message}", LogCategory.Error);
            }
        }

        private ISharePointProjectItemFile GetProjectItemFile(string fileFullPath)
        {
            ISharePointProjectItemFile projectItem = null;

            foreach (var project in this.ProjectService.Projects)
            {
                projectItem = project.ProjectItems
                    .SelectMany(folder => folder.Files)
                    .FirstOrDefault(file => file.FullPath.Equals(fileFullPath, StringComparison.OrdinalIgnoreCase));
            }

            return projectItem;
        }

        private string ReplacePathTokens(ISharePointProject project, string path)
        {
            path = path.Replace("{ProjectRoot}", Path.GetDirectoryName(project.FullPath)).Replace("\\\\", "\\");
            path = path.Replace("{SharePointRoot}", project.ProjectService.SharePointInstallPath).Replace("\\\\", "\\");

            if (path.Contains("{WebApplicationRoot}"))
            {
                var webApplicationRoot = project.SharePointConnection.ExecuteCommand<String>("Microsoft.VisualStudio.SharePoint.Commands.GetWebApplicationLocalPath");
                path = path.Replace("{WebApplicationRoot}", webApplicationRoot).Replace("\\\\", "\\");
            }

            return path;
        }
    }
}
