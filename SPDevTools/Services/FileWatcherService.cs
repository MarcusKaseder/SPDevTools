namespace SPDevTools
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Build.Evaluation;
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

        private Dictionary<Guid, Dictionary<string, string>> ProjectTokenReplacements { get; }
        private Dictionary<Guid, string[]> ProjectTokenReplacementFileExtensions { get; }

        private FileWatcherService(ISharePointProjectService projectService)
        {
            this.ProjectService = projectService;
            this.FileListener = new Dictionary<string, FileEventListener>(StringComparer.OrdinalIgnoreCase);
            this.ProjectTokenReplacements = new Dictionary<Guid, Dictionary<string, string>>();
            this.ProjectTokenReplacementFileExtensions = new Dictionary<Guid, string[]>();
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

            var deploymentPath = Path.Combine(projectItemFile.DeploymentRoot, projectItemFile.RelativePath);

            try
            {
                this.ProjectService.Logger.WriteLine($"Copy {projectItemFile.RelativePath} -> {deploymentPath}", LogCategory.Status);

                deploymentPath = this.ReplacePathTokens(projectItemFile.Project, deploymentPath);

                if (!Directory.Exists(Path.GetDirectoryName(deploymentPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(deploymentPath));
                }

                if (this.IsFileWithTokenReplacements(projectItemFile))
                {
                    var fileContent = this.ReplaceSharePointFileTokens(projectItemFile);

                    File.WriteAllText(deploymentPath, fileContent, Encoding.UTF8);
                }
                else
                {
                    File.Copy(projectItemFile.FullPath, deploymentPath, true);
                }
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

        private string ReplaceSharePointFileTokens(ISharePointProjectItemFile projectItemFile)
        {
            var tokens = this.GetSharePointFileTokenReplacements(projectItemFile.Project);
            var fileContent = File.ReadAllText(projectItemFile.FullPath);

            foreach (var token in tokens)
            {
                fileContent = fileContent.Replace(token.Key, token.Value);
            }

            return fileContent;
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

        private Dictionary<string, string> GetSharePointFileTokenReplacements(ISharePointProject project)
        {
            if (!this.ProjectTokenReplacements.ContainsKey(project.Id))
            {
                var assemblyName = AssemblyName.GetAssemblyName(project.OutputFullPath);

                if (assemblyName == null)
                {
                    this.ProjectService.Logger.ActivateOutputWindow();
                    this.ProjectService.Logger.WriteLine($"Please build the project {project.FullPath} at least one time to create the replace tokens.", LogCategory.Warning);

                    return new Dictionary<string, string>();
                }

                var tokens = new Dictionary<string, string>
                {
                    { "$SharePoint.Project.FileName$", Path.GetFileName(project.FullPath) },
                    { "$SharePoint.Project.FileNameWithoutExtension$", Path.GetFileNameWithoutExtension(project.FullPath) },
                    { "$SharePoint.Package.Name$", Path.GetFileNameWithoutExtension(project.Package.OutputPath) },
                    { "$SharePoint.Package.FileName$", Path.GetFileName(project.Package.Name) },
                    { "$SharePoint.Package.FileNameWithoutExtension$", Path.GetFileNameWithoutExtension(project.Package.Name) },
                    { "$SharePoint.Package.Id$", project.Package.Id.ToString() },
                    { "$SharePoint.Project.AssemblyFullName$", assemblyName.FullName },
                    { "$SharePoint.Project.AssemblyFileName$", Path.GetFileName(project.OutputFullPath) },
                    { "$SharePoint.Project.AssemblyFileNameWithoutExtension$", Path.GetFileNameWithoutExtension(project.OutputFullPath)},
                    { "$SharePoint.Project.AssemblyPublicKeyToken$", string.Join(string.Empty, assemblyName.GetPublicKeyToken().Select(b => $"{b:x2}"))}
                };

                this.ProjectTokenReplacements.Add(project.Id, tokens);
            }

            return this.ProjectTokenReplacements[project.Id];
        }

        private bool IsFileWithTokenReplacements(ISharePointProjectItemFile projectItemFile)
        {
            var fileExtension = Path.GetExtension(projectItemFile.Name).Replace(".", string.Empty);
            var fileExtensionsWithTokens = this.GetTokenReplacementFileExtensions(projectItemFile.Project);

            return fileExtensionsWithTokens.Contains(fileExtension, StringComparer.OrdinalIgnoreCase);
        }

        private string[] GetTokenReplacementFileExtensions(ISharePointProject project)
        {
            if (!this.ProjectTokenReplacementFileExtensions.ContainsKey(project.Id))
            {
                var loadedProject = ProjectCollection.GlobalProjectCollection.GetLoadedProjects(project.FullPath).FirstOrDefault();

                if (loadedProject == null)
                {
                    loadedProject = new Project(project.FullPath);
                }

                var tokenProjectProperty = loadedProject.GetProperty("TokenReplacementFileExtensions");
                var tokenReplacementFileExtensions = tokenProjectProperty?.EvaluatedValue
                    ?.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? new string[0];

                this.ProjectTokenReplacementFileExtensions.Add(project.Id, tokenReplacementFileExtensions);
            }

            return this.ProjectTokenReplacementFileExtensions[project.Id];
        }
    }
}
