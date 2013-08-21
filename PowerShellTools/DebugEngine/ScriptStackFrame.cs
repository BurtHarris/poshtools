using System;  
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System.Diagnostics;

namespace PowerShellTools.DebugEngine
{
    public class ScriptStackFrame : IDebugStackFrame2
    {
        private ScriptDebugger _debugger;
        private ScriptProgramNode _node;
        private ScriptDocumentContext _docContext;
        private CallStackFrame _frame;

        public CallStackFrame Frame
        {
            get { return _frame; }
        }

        public ScriptStackFrame(ScriptProgramNode node, CallStackFrame frame)
        {
            _node = node;
            _debugger = node.Debugger;
            // VS is zero based, PS is 1 based
            _docContext = new ScriptDocumentContext(frame.ScriptName, frame.ScriptLineNumber - 1, 0, frame.ToString());
            _frame = frame;
        }

        #region Implementation of IDebugStackFrame2

        public int GetCodeContext(out IDebugCodeContext2 ppCodeCxt)
        {
            Trace.WriteLine("ScriptStackFrame: GetCodeContext");
            ppCodeCxt = _docContext;
            return VSConstants.S_OK;
        }

        public int GetDocumentContext(out IDebugDocumentContext2 ppCxt)
        {
            Trace.WriteLine("ScriptStackFrame: GetDocumentContext");
            ppCxt = _docContext;
            return VSConstants.S_OK;
        }

        public int GetName(out string pbstrName)
        {
            Trace.WriteLine("ScriptStackFrame: GetName");
            pbstrName = _docContext.ToString();
            return VSConstants.S_OK;
        }

        public int GetInfo(enum_FRAMEINFO_FLAGS dwFieldSpec, uint nRadix, FRAMEINFO[] pFrameInfo)
        {
            var frameInfo = pFrameInfo[0];

            Trace.WriteLine("ScriptStackFrame: GetInfo");


            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_FUNCNAME) != 0)
            {
                frameInfo.m_bstrFuncName = this.ToString();
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_FUNCNAME;
            }

            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_MODULE) != 0)
            {
                frameInfo.m_pModule = _node;
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_MODULE;
            }

            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_DEBUG_MODULEP) != 0)
            {
                frameInfo.m_pModule = _node;
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_DEBUG_MODULEP;
            }


            // The debugger is requesting the IDebugStackFrame2 value for this frame info.
            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_FUNCNAME_MODULE) != 0)
            {
                frameInfo.m_bstrModule = "Module";
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_FUNCNAME_MODULE;
            }

            // The debugger is requesting the IDebugStackFrame2 value for this frame info.
            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_FRAME) != 0)
            {
                frameInfo.m_pFrame = this;
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_FRAME;
            }

            // Does this stack frame of symbols loaded?
            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_DEBUGINFO) != 0)
            {
                frameInfo.m_fHasDebugInfo = 1;
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_DEBUGINFO;
            }

            // Is this frame stale?
            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_STALECODE) != 0)
            {
                frameInfo.m_fStaleCode = 0;
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_STALECODE;
            }

            if ((dwFieldSpec & enum_FRAMEINFO_FLAGS.FIF_LANGUAGE) != 0)
            {
                frameInfo.m_bstrLanguage = "PowerShell";
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_LANGUAGE;
            }

            pFrameInfo[0] = frameInfo;

            return VSConstants.S_OK;
        }

        public int GetPhysicalStackRange(out ulong paddrMin, out ulong paddrMax)
        {
            Trace.WriteLine("ScriptStackFrame: GetPhysicalStackRange");
            paddrMin = 0;
            paddrMax = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int GetExpressionContext(out IDebugExpressionContext2 ppExprCxt)
        {
            Trace.WriteLine("ScriptStackFrame: GetExpressionContext");
            ppExprCxt = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetLanguageInfo(ref string pbstrLanguage, ref Guid pguidLanguage)
        {
            Trace.WriteLine("ScriptStackFrame: GetLanguageInfo");
            pguidLanguage = Guid.Empty;
            pbstrLanguage = "PowerShell";
            return VSConstants.S_OK;
        }

        public int GetDebugProperty(out IDebugProperty2 ppProperty)
        {
            Trace.WriteLine("ScriptStackFrame: GetDebugProperty");
            ppProperty = null;
            return VSConstants.E_NOTIMPL;
        }

        public int EnumProperties(enum_DEBUGPROP_INFO_FLAGS dwFields, uint nRadix, ref Guid guidFilter, uint dwTimeout, out uint pcelt, out IEnumDebugPropertyInfo2 ppEnum)
        {
            Trace.WriteLine("ScriptStackFrame: EnumProperties");
            pcelt = 0;
            ppEnum = new ScriptPropertyCollection(_debugger);
            return VSConstants.S_OK;
        }

        public int GetThread(out IDebugThread2 ppThread)
        {
            Trace.WriteLine("ScriptStackFrame: GetThread!!!!!!!!!!!!!!!!!!");
            ppThread = null;
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        public override string ToString()
        {
            return _docContext.ToString();
        }
    }

    public class ScriptStackFrameCollection : List<ScriptStackFrame>, IEnumDebugFrameInfo2
    {
        private uint _iterationLocation;
        private ScriptProgramNode _node;

        public ScriptStackFrameCollection(IEnumerable<ScriptStackFrame> frames, ScriptProgramNode node)
        {
            foreach (var frame in frames)
            {
                Add(frame);
            }

            _iterationLocation = 0;
            _node = node;
        }

        #region Implementation of IEnumDebugFrameInfo2

        public int Next(uint celt, FRAMEINFO[] rgelt, ref uint pceltFetched)
        {
            pceltFetched = 0;

            if (_iterationLocation == Count) return VSConstants.S_FALSE;  
            if (celt == 0) return VSConstants.S_OK;

            var currentIteration = _iterationLocation;

            for (uint i = currentIteration; i < currentIteration + celt; i++)
            {
                var currentFrame = this[(int)i];

                var index = i - currentIteration;

                Trace.WriteLine("ScriptStackFrameCollection: Next");
                rgelt[index].m_dwValidFields = (enum_FRAMEINFO_FLAGS.FIF_LANGUAGE | enum_FRAMEINFO_FLAGS.FIF_DEBUGINFO | enum_FRAMEINFO_FLAGS.FIF_STALECODE | enum_FRAMEINFO_FLAGS.FIF_FRAME | enum_FRAMEINFO_FLAGS.FIF_FUNCNAME | enum_FRAMEINFO_FLAGS.FIF_MODULE);
                rgelt[index].m_fHasDebugInfo = 1;
                rgelt[index].m_fStaleCode = 0; 
                rgelt[index].m_bstrLanguage = "PowerShell";
                rgelt[index].m_bstrFuncName = currentFrame.ToString();
                rgelt[index].m_pFrame = currentFrame;
                rgelt[index].m_pModule = _node;

                pceltFetched++;
                _iterationLocation++;
            }

            return VSConstants.S_OK;
        }

        public int Skip(uint celt)
        {
            Trace.WriteLine("ScriptStackFrameCollection: Skip");
            _iterationLocation += celt;
            return VSConstants.S_OK;
        }

        public int Reset()
        {
            Trace.WriteLine("ScriptStackFrameCollection: Reset");
            _iterationLocation = 0;
            return VSConstants.S_OK;
        }

        public int Clone(out IEnumDebugFrameInfo2 ppEnum)
        {
            Trace.WriteLine("ScriptStackFrameCollection: Clone");
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetCount(out uint pcelt)
        {
            Trace.WriteLine("ScriptStackFrameCollection: GetCount");
            pcelt = (uint)Count;
            return VSConstants.S_OK;
        }

        #endregion
    }
}
