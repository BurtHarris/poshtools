using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Threading;
using log4net;

namespace PowerShellTools.DebugEngine
{
    public class EventArgs<T> : EventArgs
    {
        public EventArgs(T value)
        {
            Value = value;
        }

        public T Value { get; private set; }
    }

    public class ScriptDebugger 
    {
        public event Action<string> DocumentChanged;
        private Runspace _runspace;

        private List<ScriptBreakpoint> _breakpoints;
        private List<ScriptStackFrame> _callstack;
        private PowerShell _currentPowerShell;

        public event EventHandler<EventArgs<ScriptBreakpoint>> BreakpointHit;
        public event EventHandler<EventArgs<ScriptLocation>> DebuggerPaused;
        public event EventHandler<BreakpointUpdatedEventArgs> BreakpointUpdated;
        public event EventHandler<EventArgs<string>> OutputString;
        public event EventHandler DebuggingFinished;

        private AutoResetEvent _pausedEvent = new AutoResetEvent(false);
        private DebuggerResumeAction _resumeAction;

        public IDictionary<string, object> Variables { get; private set; }
        public IEnumerable<ScriptStackFrame> CallStack { get { return _callstack; } }
        public ScriptProgramNode CurrentExecutingNode { get; private set; }

        private static readonly ILog Log = LogManager.GetLogger(typeof (ScriptDebugger));

        public void OnDocumentChanged(string fileName)
        {
            Log.InfoFormat("OnDocumentChanged: {0}", fileName);
            if (DocumentChanged != null)
            {
                DocumentChanged(fileName);
            }
        }

        public ScriptDebugger(Runspace runspace, IEnumerable<ScriptBreakpoint> initialBreakpoints )
        {
            Log.InfoFormat("ScriptDebugger: Initial Breakpoints: {0}", initialBreakpoints.Count());

            _runspace = runspace;
            _runspace.Debugger.DebuggerStop += Debugger_DebuggerStop;
            _runspace.Debugger.BreakpointUpdated += Debugger_BreakpointUpdated;
            _runspace.StateChanged += _runspace_StateChanged;
            _breakpoints = new List<ScriptBreakpoint>();

            ClearBreakpoints();

            foreach (var bp in initialBreakpoints)
            {
                SetBreakpoint(bp);
                _breakpoints.Add(bp);
                bp.Bind();
            }
        }

        ~ScriptDebugger()
        {
            DebuggerFinished();
        }

        private void ClearBreakpoints()
        {
            Log.Info("ClearBreakpoints");

            IEnumerable<PSObject> breakpoints;
            using (var pipeline = new LockablePipeline(_runspace.CreatePipeline()))
            {
                breakpoints = pipeline.InvokeCommand("Get-PSBreakpoint");
            }

            if (!breakpoints.Any()) return;

            Log.InfoFormat("Clearing {0} breakpoints.", breakpoints.Count());

            using (var pipeline = new LockablePipeline(_runspace.CreatePipeline()))
            {
                var command = new Command("Remove-PSBreakpoint");
                command.Parameters.Add("Breakpoint", breakpoints);

                pipeline.InvokeCommand(command);
            }
        }

        private void SetBreakpoint(ScriptBreakpoint breakpoint)
        {
            Log.InfoFormat("SetBreakpoint: {0} {1} {2}", breakpoint.File, breakpoint.Line, breakpoint.Column);

            using (var pipeline = new LockablePipeline(_runspace.CreatePipeline()))
            {
                var command = new Command("Set-PSBreakpoint");
                command.Parameters.Add("Script", breakpoint.File);
                command.Parameters.Add("Line", breakpoint.Line);

                pipeline.InvokeCommand(command);
            }
        }

        void _runspace_StateChanged(object sender, RunspaceStateEventArgs e)
        {
            Log.InfoFormat("Runspace State Changed: {0}", e.RunspaceStateInfo.State);

            switch (e.RunspaceStateInfo.State)
            {
                case RunspaceState.Broken:
                case RunspaceState.Closed:
                case RunspaceState.Disconnected:
                    if (DebuggingFinished != null)
                    {
                        DebuggingFinished(this, new EventArgs());
                    }
                    break;
            }
        }

        void Debugger_BreakpointUpdated(object sender, BreakpointUpdatedEventArgs e)
        {
            Log.InfoFormat("Breakpoint updated: {0} {1}", e.UpdateType, e.Breakpoint);

            if (BreakpointUpdated != null)
            {
                BreakpointUpdated(sender, e);
            }
        }

        void Debugger_DebuggerStop(object sender, DebuggerStopEventArgs e)
        {
            Log.InfoFormat("Debugger stopped");

            RefreshScopedVariables();
            RefreshCallStack();

            if (e.Breakpoints.Count == 0 || !ProcessLineBreakpoints(e))
            {
                if (DebuggerPaused != null)
                {
                    var scriptLocation = new ScriptLocation();
                    scriptLocation.File = e.InvocationInfo.ScriptName;
                    scriptLocation.Line = e.InvocationInfo.ScriptLineNumber;
                    scriptLocation.Column = 0;

                    DebuggerPaused(this, new EventArgs<ScriptLocation>(scriptLocation));
                }
            }

            //Wait for the user to step, continue or stop
            _pausedEvent.WaitOne();
            e.ResumeAction = _resumeAction;
        }

        private bool ProcessLineBreakpoints(DebuggerStopEventArgs e)
        {
            Log.InfoFormat("Process Line Breapoints");

            var lbp = e.Breakpoints[0] as LineBreakpoint;
            if (lbp != null)
            {
                var bp =
                    _breakpoints.FirstOrDefault(
                        m =>
                        m.Column == lbp.Column && lbp.Line == m.Line &&
                        lbp.Script.Equals(m.File, StringComparison.InvariantCultureIgnoreCase));

                if (bp != null)
                {
                    if (BreakpointHit != null)
                    {
                        BreakpointHit(this, new EventArgs<ScriptBreakpoint>(bp));
                        return true;
                    }
                }
            }

            return false;
        }

        public void Stop()
        {
            Log.Info("Stop");

            _resumeAction = DebuggerResumeAction.Stop;
            _pausedEvent.Set();
            _currentPowerShell.Stop();
            DebuggerFinished();
        }

        public void StepOver()
        {
            Log.Info("StepOver");
            _resumeAction = DebuggerResumeAction.StepOver;
            _pausedEvent.Set();
        }

        public void StepInto()
        {
            Log.Info("StepInto");
            _resumeAction = DebuggerResumeAction.StepInto;
            _pausedEvent.Set();
        }

        public void StepOut()
        {
            Log.Info("StepOut");
            _resumeAction = DebuggerResumeAction.StepOut;
            _pausedEvent.Set();
        }

        public void Continue()
        {
            Log.Info("Continue");
            _resumeAction = DebuggerResumeAction.Continue;
            _pausedEvent.Set();
        }

        public void Execute(ScriptProgramNode node)
        {
            Log.Info("Execute");
            CurrentExecutingNode = node;
 
            try
            {
                using (_currentPowerShell = PowerShell.Create())
                {
                    _currentPowerShell.Runspace = _runspace;

                    var psCommand = new PSCommand();
                    psCommand.AddScript(". '" + node.FileName + "'");
                    psCommand.Commands[0].MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);
                    //psCommand.AddCommand(new Command("out-default"));

                    _currentPowerShell.Commands = psCommand;

                    var objects = new PSDataCollection<PSObject>();
                    objects.DataAdded +=objects_DataAdded;

                    _currentPowerShell.Invoke<PSObject>(null, objects);
                }
            }
            catch (Exception ex)
            {
                Log.Info("Terminating error", ex);
                if (OutputString != null)
                {
                    OutputString(this, new EventArgs<string>("Error: " + ex.Message + Environment.NewLine));
                }
            }
            finally
            {
                DebuggerFinished();
            }

        }

        void objects_DataAdded(object sender, DataAddedEventArgs e)
        {
            if (OutputString != null)
            {
                var list =  sender as PSDataCollection<PSObject>;
                OutputString(this, new EventArgs<string>(list[e.Index].ToString() + Environment.NewLine));
            }
        }

        private void RefreshScopedVariables()
        {
            IEnumerable<PSObject> result = null;
            using (var pipeline = new LockablePipeline(_runspace.CreateNestedPipeline()))
            {
                result = pipeline.InvokeCommand("Get-Variable");
            }

            Variables = new Dictionary<string, object>();

            foreach (var psobj in result)
            {
                var psVar = psobj.BaseObject as PSVariable;

                if (psVar != null)
                {
                    Variables.Add(psVar.Name, psVar.Value);    
                }
            }
        }

        private void RefreshCallStack()
        {
            IEnumerable<PSObject> result = null;
            using (var pipeline = new LockablePipeline(_runspace.CreateNestedPipeline()))
            {
                result = pipeline.InvokeCommand("Get-PSCallstack");
            }

            _callstack = new List<ScriptStackFrame>();

            foreach (var psobj in result)
            {
                var frame = psobj.BaseObject as CallStackFrame;
                if (frame != null)
                {
                    _callstack.Add(new ScriptStackFrame(CurrentExecutingNode, frame));
                }
            }
        }

        private void DebuggerFinished()
        {
            _runspace.Debugger.DebuggerStop -= Debugger_DebuggerStop;
            _runspace.Debugger.BreakpointUpdated -= Debugger_BreakpointUpdated;
            _runspace.StateChanged -= _runspace_StateChanged;
            if (DebuggingFinished != null)
            {
                DebuggingFinished(this, new EventArgs());
            }
        }
    }

    public class LockablePipeline : IDisposable
    {
        private Mutex _mutex;
        public Pipeline Pipeline { get; private set; }

        public LockablePipeline(Pipeline pipeline)
        {
            _mutex = new Mutex(true);
            Pipeline = pipeline;
        }

        public IEnumerable<PSObject> InvokeCommand(Command command)
        {
            Pipeline.Commands.Add(command);
            return Pipeline.Invoke();
        }

        public IEnumerable<PSObject> InvokeCommand(string command)
        {
            Pipeline.Commands.Add(command);
            return Pipeline.Invoke();
        }

        public void InvokeScript(string fileName)
        {
            Pipeline.Commands.AddScript(String.Format(". '{0}'", fileName));
            Pipeline.Invoke();
        }

        public void Dispose()
        {
            Pipeline.Dispose();
            _mutex.ReleaseMutex();
        }
    }

    public class ScriptLocation
    {
        public string File { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }
}