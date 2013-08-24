using System.Diagnostics;
using System.Management.Automation.Runspaces;
using log4net;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System.Runtime.InteropServices;

namespace PowerShellTools.DebugEngine
{
    public class ScriptBreakpoint : IDebugBoundBreakpoint2, IEnumDebugBoundBreakpoints2, IDebugPendingBreakpoint2, IDebugBreakpointResolution2
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof (ScriptBreakpoint));

        private IEngineEvents _callback;
        private ScriptProgramNode _node;
        private Runspace _runspace;
        private string _file;
        private int _line;

        public int Line
        {
            get { return _line; }
        }

        public int Column
        {
            get { return _column; }
        }

        public string File
        {
            get { return _file; }
        }

        private int _column;

        public ScriptBreakpoint(ScriptProgramNode node, string file, int line, int column, IEngineEvents callback, Runspace runspace)
        {
            Log.InfoFormat("ScriptBreakPoint: {0} {1} {2}", file, line, column);

            _node = node;
            _callback = callback;
            _runspace = runspace;
            _line = line;
            _column = column;
            _file = file;
        }

        #region Implementation of IDebugBoundBreakpoint2

        public int GetPendingBreakpoint(out IDebugPendingBreakpoint2 ppPendingBreakpoint)
        {
            Log.Info("ScriptBreakpoint: GetPendingBreakpoint");
            ppPendingBreakpoint = this;
            return VSConstants.S_OK;
        }

        public int GetState(enum_BP_STATE[] pState)
        {
            Log.Info("ScriptBreakpoint: IDebugBoundBreakpoint2:GetState");
            pState[0] = enum_BP_STATE.BPS_ENABLED;
            return VSConstants.S_OK;
        }

        public int GetHitCount(out uint pdwHitCount)
        {
            Log.Info("ScriptBreakpoint: GetHitCount");
            pdwHitCount = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int GetBreakpointResolution(out IDebugBreakpointResolution2 ppBPResolution)
        {
            Log.Info("ScriptBreakpoint: GetBreakpointResolution");
            ppBPResolution = this;
            return VSConstants.S_OK;
        }

        public int Enable(int fEnable)
        {
            Log.Info("ScriptBreakpoint: Enable");
            return VSConstants.S_OK;
        }

        public int SetHitCount(uint dwHitCount)
        {
            Log.Info("ScriptBreakpoint: SetHitCount");
            return VSConstants.E_NOTIMPL;
        }

        public int SetCondition(BP_CONDITION bpCondition)
        {
            Log.Info("ScriptBreakpoint: SetCondition");
            return VSConstants.E_NOTIMPL;
        }

        public int SetPassCount(BP_PASSCOUNT bpPassCount)
        {
            Log.Info("ScriptBreakpoint: SetPassCount");
            return VSConstants.E_NOTIMPL;
        }

        public int Delete()
        {
            Log.Info("ScriptBreakpoint: Delete");

            return VSConstants.S_OK;
        }

        #endregion

        #region Implementation of IEnumDebugBoundBreakpoints2

        public int Next(uint celt, IDebugBoundBreakpoint2[] rgelt, ref uint pceltFetched)
        {
            Log.Info("ScriptBreakpoint: Next");
            rgelt[0] = this;
            pceltFetched = 1;
            return VSConstants.S_OK;
        }

        public int Skip(uint celt)
        {
            Log.Info("ScriptBreakpoint: Skip");
            return VSConstants.E_NOTIMPL;
        }

        public int Reset()
        {
            Log.Info("ScriptBreakpoint: Reset");
            return VSConstants.S_OK;
        }

        public int Clone(out IEnumDebugBoundBreakpoints2 ppEnum)
        {
            Log.Info("ScriptBreakpoint: Clone");
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetCount(out uint pcelt)
        {
            Log.Info("ScriptBreakpoint: GetCount");
            pcelt = 1;
            return VSConstants.S_OK;
        }

        #endregion

        #region Implementation of IDebugPendingBreakpoint2

        public int CanBind(out IEnumDebugErrorBreakpoints2 ppErrorEnum)
        {
            Log.Info("ScriptBreakpoint: CanBind");
            ppErrorEnum = null;
            return VSConstants.S_OK;
        }

        public int Bind()
        {
            Log.Info("ScriptBreakpoint: Bind");
            _callback.Breakpoint(_node, this);
            return VSConstants.S_OK;
        }

        public int GetState(PENDING_BP_STATE_INFO[] pState)
        {
            Log.Info("ScriptBreakpoint: IDebugPendingBreakpoint2:GetState");
            var state = new PENDING_BP_STATE_INFO
                            {
                                state = enum_PENDING_BP_STATE.PBPS_ENABLED,
                                Flags = enum_PENDING_BP_STATE_FLAGS.PBPSF_NONE
                            };

            pState[0] = state;
            return VSConstants.S_OK;
        }

        public int GetBreakpointRequest(out IDebugBreakpointRequest2 ppBPRequest)
        {
            Log.Info("ScriptBreakpoint: GetBreakpointRequest");
            ppBPRequest = null;
            return VSConstants.S_OK;
        }

        public int Virtualize(int fVirtualize)
        {
            Log.Info("ScriptBreakpoint: Virtualize");
            return VSConstants.S_OK;
        }

        public int EnumBoundBreakpoints(out IEnumDebugBoundBreakpoints2 ppEnum)
        {
            Log.Info("ScriptBreakpoint: EnumBoundBreakpoints");
            ppEnum = this;
            return VSConstants.S_OK;
        }

        public int EnumErrorBreakpoints(enum_BP_ERROR_TYPE bpErrorType, out IEnumDebugErrorBreakpoints2 ppEnum)
        {
            Log.Info("ScriptBreakpoint: EnumErrorBreakpoints");
            ppEnum = null;
            return VSConstants.S_OK;
        }

        #endregion

        #region Implementation of IDebugBreakpointResolution2

        public int GetBreakpointType(enum_BP_TYPE[] pBPType)
        {
            Log.Info("ScriptBreakpoint: GetBreakpointType");
            pBPType[0] = enum_BP_TYPE.BPT_CODE;
            return VSConstants.S_OK;
        }
         

        public int GetResolutionInfo(enum_BPRESI_FIELDS dwFields, BP_RESOLUTION_INFO[] pBPResolutionInfo)
        {
            //VS line\column is zero based. PowerShell is 1
            var documentContext = new ScriptDocumentContext(File, Line - 1, Column, "");

            Log.Info("ScriptBreakpoint: GetResolutionInfo");
            if (dwFields == enum_BPRESI_FIELDS.BPRESI_ALLFIELDS)
            {
                var loc = new BP_RESOLUTION_LOCATION
                              {
                                  bpType = (uint)enum_BP_TYPE.BPT_CODE,
                                  unionmember1 = Marshal.GetComInterfaceForObject(documentContext, typeof(IDebugCodeContext2))
                              };

                pBPResolutionInfo[0].bpResLocation = loc;
                pBPResolutionInfo[0].pProgram = _node;
                pBPResolutionInfo[0].pThread = _node;
            }

            return VSConstants.S_OK;
        }

        #endregion
    }
}
