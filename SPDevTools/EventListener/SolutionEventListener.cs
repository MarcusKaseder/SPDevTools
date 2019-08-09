using Microsoft;

namespace SPDevTools.EventListener
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.ProjectSystem;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;

    public class SolutionEventListener : IVsUpdateSolutionEvents
    {
        public static SolutionEventListener Current { get; private set; }

        public HierarchyEventListener Events { get; set; }

        public uint SubscriptionCookie { get; private set; }

        private SolutionEventListener(IVsSolutionBuildManager solutionManager)
        {
            solutionManager.AdviseUpdateSolutionEvents(this, out var cookie);

            this.SubscriptionCookie = cookie;
        }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            var solutionManager = serviceProvider.GetService(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager;

            SolutionEventListener.Current = new SolutionEventListener(solutionManager);
        }

        int IVsUpdateSolutionEvents.UpdateSolution_Begin(ref int pfCancelUpdate)
        {
            return VSConstants.S_OK;
        }

        int IVsUpdateSolutionEvents.UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
        {
            return VSConstants.S_OK;
        }

        int IVsUpdateSolutionEvents.UpdateSolution_StartUpdate(ref int pfCancelUpdate)
        {
            return VSConstants.S_OK;
        }

        int IVsUpdateSolutionEvents.UpdateSolution_Cancel()
        {
            return VSConstants.S_OK;
        }

        int IVsUpdateSolutionEvents.OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy)
        {
            if (pIVsHierarchy == null)
            {
                return VSConstants.S_FALSE;
            }

            this.Events = new HierarchyEventListener(pIVsHierarchy);

            return VSConstants.S_OK;
        }
    }
}
