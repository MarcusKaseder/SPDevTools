namespace SPDevTools.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Threading;
    using EnvDTE;
    using EnvDTE80;
    using Microsoft.VisualStudio.SharePoint;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using SPDevTools.EventListener;

    [Export(typeof(ISharePointProjectExtension))]

    internal class AutoCopyProjectExtension : ISharePointProjectExtension
    {
        public void Initialize(ISharePointProjectService projectService)
        {
            if (!projectService.IsSharePointInstalled)
            {
                return;
            }

            FileWatcherService.Initialize(projectService);
            SolutionEventListener.Initialize(projectService.ServiceProvider);
        }
    }
}
