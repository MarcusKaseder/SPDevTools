namespace SPDevTools.EventListener
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.SharePoint;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;

    public class FileEventListener : IVsFileChangeEvents
    {
        public string FileFullPath { get; }

        private IVsFileChangeEx FileChangeService { get; }

        private Action<FileEventListener> FileChangedCallback { get; }

        private uint SubscriptionCookie { get; set; }

        public FileEventListener(string fileFullPath, IVsFileChangeEx fileChangeService, Action<FileEventListener> fileChangedCallback)
        {
            this.FileFullPath = fileFullPath;
            this.FileChangeService = fileChangeService;
            this.FileChangedCallback = fileChangedCallback;

            this.FileChangeService.AdviseFileChange(fileFullPath, (uint)_VSFILECHANGEFLAGS.VSFILECHG_Time, this, out var cookie);
            this.SubscriptionCookie = cookie;
        }

        public void Unadvice()
        {
            this.FileChangeService.UnadviseFileChange(this.SubscriptionCookie);
        }

        int IVsFileChangeEvents.FilesChanged(uint cChanges, string[] rgpszFile, uint[] rggrfChange)
        {
            this.FileChangedCallback?.Invoke(this);
            return VSConstants.S_OK;
        }

        int IVsFileChangeEvents.DirectoryChanged(string pszDirectory)
        {
            return VSConstants.S_OK;
        }
    }
}
