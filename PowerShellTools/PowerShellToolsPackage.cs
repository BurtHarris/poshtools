using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Navigation;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using PowerShellTools.DebugEngine;
using PowerShellTools.LanguageService;
using PowerShellTools.Project;
using log4net;

namespace PowerShellTools
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideAutoLoad(UIContextGuids.NoSolution)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists)]
    [ProvideLanguageService(typeof(PowerShellLanguageInfo), "PowerShell", 101, ShowDropDownOptions = true)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // This attribute registers a tool window exposed by this package.
    [ProvideToolWindow(typeof(MyToolWindow))]
    [Guid(GuidList.PowerShellToolsPackageGuid)]
    [ProvideObject(typeof(PowerShellGeneralPropertyPage))]
    [ProvideObject(typeof(PowerShellModulePropertyPage))]
    [Microsoft.VisualStudio.Shell.ProvideDebugEngine("{43ACAB74-8226-4920-B489-BFCF05372437}", "PowerShell", PortSupplier = "{708C1ECA-FF48-11D2-904F-00C04FA302A1}", ProgramProvider = "{08F3B557-C153-4F6C-8745-227439E55E79}", Attach = true, CLSID = "{C7F9F131-53AB-4FD0-8517-E54D124EA392}")]
    [Clsid(Clsid = "{C7F9F131-53AB-4FD0-8517-E54D124EA392}", Assembly = "PowerGuiVsx.Core.DebugEngine", Class = "PowerGuiVsx.Core.DebugEngine.Engine")]
    [Clsid(Clsid = "{08F3B557-C153-4F6C-8745-227439E55E79}", Assembly = "PowerGuiVsx.Core.DebugEngine", Class = "PowerGuiVsx.Core.DebugEngine.ScriptProgramProvider")]
    [Microsoft.VisualStudioTools.ProvideDebugEngine("PowerShell", typeof(ScriptProgramProvider), typeof(Engine), "{43ACAB74-8226-4920-B489-BFCF05372437}")]
    [ProvideIncompatibleEngineInfo("{92EF0900-2251-11D2-B72E-0000F87572EF}")]
    //[ProvideIncompatibleEngineInfo("{449EC4CC-30D2-4032-9256-EE18EB41B62B}")]
    //[ProvideIncompatibleEngineInfo("{449EC4CC-30D2-4032-9256-EE18EB41B62B}")]
    [ProvideIncompatibleEngineInfo("{F200A7E7-DEA5-11D0-B854-00A0244A1DE2}")]
    public sealed class PowerShellToolsPackage : CommonPackage
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PowerShellToolsPackage));

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public PowerShellToolsPackage()
        {
            Log.Info(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
            Instance = this;
        }

        /// <summary>
        /// Returns the PowerShell host for the package.
        /// </summary>
        internal VSXHost Host { get; private set; }
        /// <summary>
        /// Returns the current package instance.
        /// </summary>
        public static PowerShellToolsPackage Instance { get; private set; }

        /// <summary>
        /// This function is called when the user clicks the menu item that shows the 
        /// tool window. See the Initialize method to see how the menu item is associated to 
        /// this function using the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void ShowToolWindow(object sender, EventArgs e)
        {
            // Get the instance number 0 of this tool window. This window is single instance so this instance
            // is actually the only one.
            // The last flag is set to true so that if the tool window does not exists it will be created.
            ToolWindowPane window = this.FindToolWindow(typeof(MyToolWindow), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException("Couldn't create tool window");
            }
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }


        public override Type GetLibraryManagerType()
        {
            return null;
        }

        internal override LibraryManager CreateLibraryManager(CommonPackage package)
        {
            throw new NotImplementedException();
        }

        public override bool IsRecognizedFile(string filename)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Log.Info (string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));
            base.Initialize();

            var langService = new PowerShellLanguageInfo(this);
            ((IServiceContainer)this).AddService(langService.GetType(), langService, true);

            // Add our command handlers for menu (commands must exist in the .vsct file)
            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if ( null != mcs )
            {
                // Create the command for the tool window
                CommandID toolwndCommandID = new CommandID(GuidList.guidPowerGUIVSXCmdSet, (int)PkgCmdIDList.cmdidPowerGUIVSXConsole);
                MenuCommand menuToolWin = new MenuCommand(ShowToolWindow, toolwndCommandID);
                mcs.AddCommand( menuToolWin );
            }

            InitializePowerShellHost();
        }

        /// <summary>
        /// Initialize the PowerShell host.
        /// </summary>
        private void InitializePowerShellHost()
        {
            Host = new VSXHost();
            Host.HostUi.OutputProgress = (label, percentage) =>
            {
                var statusBar = (IVsStatusbar) GetService(typeof (SVsStatusbar));
                uint cookie = 0;
                statusBar.Progress(ref cookie, 1, label, (uint) percentage, 100);

                if (percentage == 100)
                {
                    statusBar.Progress(ref cookie, 1, "", 0, 0);    
                }
            };
        }



    }
}
