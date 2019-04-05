namespace SPDevTools.EventListener
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Shell.Interop;

    public class HierarchyEventListener : IVsHierarchyEvents
    {
        public static HierarchyEventListener Current { get; private set; }

        public IVsHierarchy VsHierarchy { get; }

        public uint SubscriptionCookie { get; }
        
        public HierarchyEventListener(IVsHierarchy vsHierarchy)
        {
            this.VsHierarchy = vsHierarchy;
            this.VsHierarchy.AdviseHierarchyEvents(this, out var cookie);

            this.SubscriptionCookie = cookie;
        }

        public static void Initialize(IVsHierarchy vsHierarchy)
        {
            HierarchyEventListener.Current?.Unadvice();

            HierarchyEventListener.Current = new HierarchyEventListener(vsHierarchy);
        }

        public void Unadvice()
        {
            this.VsHierarchy.UnadviseHierarchyEvents(this.SubscriptionCookie);
        }

        int IVsHierarchyEvents.OnItemAdded(uint itemidParent, uint itemidSiblingPrev, uint itemidAdded)
        {
            this.VsHierarchy.GetCanonicalName(itemidAdded, out var name);

            FileWatcherService.Current.AddFile(name);

            return VSConstants.S_OK;
        }

        int IVsHierarchyEvents.OnItemsAppended(uint itemidParent)
        {
            return VSConstants.S_OK;
        }

        int IVsHierarchyEvents.OnItemDeleted(uint itemid)
        {
            this.VsHierarchy.GetCanonicalName(itemid, out var name);

            FileWatcherService.Current.RemoveFile(name);

            return VSConstants.S_OK;
        }

        int IVsHierarchyEvents.OnPropertyChanged(uint itemid, int propid, uint flags)
        {
            return VSConstants.S_OK;
        }

        int IVsHierarchyEvents.OnInvalidateItems(uint itemidParent)
        {
            return VSConstants.S_OK;
        }

        int IVsHierarchyEvents.OnInvalidateIcon(IntPtr hicon)
        {
            return VSConstants.S_OK;
        }
    }
}
