using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CopyAndReplace.Implementation;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace CopyAndReplace {
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(GuidList.PackageString)]
    public sealed class CopyAndReplacePackage : AsyncPackage {
        private Controller controller;
        private DTE2 dte;
        private ProjectItemsEvents csItemsEvents;
        private CommandEvents pasteEvent;
        private IDebugLogger logger;

        public CopyAndReplacePackage() {
            Trace.WriteLine(string.Format("Entering constructor for: {0}", this), VSPackageInfo.Name);
        }

        protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
            Trace.WriteLine(string.Format("Entering Initialize() of: {0}", this), VSPackageInfo.Name);
            await base.InitializeAsync(cancellationToken, progress);

            this.dte = (DTE2)GetService(typeof(DTE));
            this.InitializeLogger();

            var container = GetComponentModel();
            var fileFactory = new TextDocumentWrapperFactory(container.GetService<IFileExtensionRegistryService>(), container.GetService<ITextDocumentFactoryService>());
            this.controller = new Controller(fileFactory, this.logger);

            this.pasteEvent = this.dte.Events.CommandEvents[typeof(VSConstants.VSStd97CmdID).GUID.ToString("B"), (int)VSConstants.VSStd97CmdID.Paste];
            this.pasteEvent.BeforeExecute += delegate { LogExceptions(() => controller.HandleBeforePaste()); };
            this.pasteEvent.AfterExecute += delegate { LogExceptions(() => controller.HandleAfterPaste()); };

            this.csItemsEvents = (ProjectItemsEvents)dte.Events.GetObject("CSharpProjectItemsEvents");
            this.csItemsEvents.ItemAdded += item => LogExceptions(() => controller.HandleAddedItem(item));
        }

        private void InitializeLogger() {
            var output = this.GetOutputPane(GuidList.OutputPane, VSPackageInfo.Name + " (Diagnostic)");
            this.logger = new DebugLogger(output, VSPackageInfo.Name);
        } 
        
        private static IComponentModel GetComponentModel() {
            var dte2 = (DTE2)Package.GetGlobalService(typeof(SDTE));
            var serviceProvider = new ServiceProvider((Microsoft.VisualStudio.OLE.Interop.IServiceProvider)dte2);
            return (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));
        }

        private void LogExceptions(Action action) {
            try {
                action();
            }
            catch (Exception ex) {
                this.logger.WriteLine("Exception: {0}", ex);
                throw;
            }
        }
    }
}