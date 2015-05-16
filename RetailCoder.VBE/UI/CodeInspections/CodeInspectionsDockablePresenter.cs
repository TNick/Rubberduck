﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Vbe.Interop;
using Rubberduck.Inspections;
using Rubberduck.Parsing;
using Rubberduck.VBEditor.Extensions;

namespace Rubberduck.UI.CodeInspections
{
    public class CodeInspectionsDockablePresenter : DockablePresenterBase
    {
        private CodeInspectionsWindow Control { get { return UserControl as CodeInspectionsWindow; } }

        private IEnumerable<VBProjectParseResult> _parseResults;
        private IList<ICodeInspectionResult> _results;
        private readonly IInspector _inspector;

        /// <summary>
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="_inspector_Reset"/> is <c>null</c>.</exception>
        /// <param name="inspector"></param>
        /// <param name="vbe"></param>
        /// <param name="addin"></param>
        /// <param name="window"></param>
        public CodeInspectionsDockablePresenter(IInspector inspector, VBE vbe, AddIn addin, CodeInspectionsWindow window)
            :base(vbe, addin, window)
        {
            _inspector = inspector;
            _inspector.IssuesFound += _inspector_IssuesFound;
            _inspector.Reset += _inspector_Reset;
            _inspector.Parsing += _inspector_Parsing;
            _inspector.ParseCompleted += _inspector_ParseCompleted;

            Control.RefreshCodeInspections += Control_RefreshCodeInspections;
            Control.NavigateCodeIssue += Control_NavigateCodeIssue;
            Control.QuickFix += Control_QuickFix;
            Control.CopyResults += Control_CopyResultsToClipboard;
        }

        private void _inspector_ParseCompleted(object sender, ParseCompletedEventArgs e)
        {
            if (sender != this)
            {
                return;
            }

            ToggleParsingStatus(false);
            _parseResults = e.ParseResults;
        }

        private void _inspector_Parsing(object sender, EventArgs e)
        {
            if (sender != this)
            {
                return;
            }

            ToggleParsingStatus();
            Control.Invoke((MethodInvoker) delegate
            {
                Control.EnableRefresh(false);
            });
        }

        private void ToggleParsingStatus(bool isParsing = true)
        {
            Control.Invoke((MethodInvoker) delegate
            {
                Control.ToggleParsingStatus(isParsing);
            });
        }

        private void Control_CopyResultsToClipboard(object sender, EventArgs e)
        {
            var results = string.Join("\n", _results.Select(FormatResultForClipboard));
            var text = string.Format("Rubberduck Code Inspections - {0}\n{1} issue" + (_results.Count != 1 ? "s" : string.Empty) + " found.\n",
                            DateTime.Now, _results.Count) + results;

            Clipboard.SetText(text);
        }

        private string FormatResultForClipboard(ICodeInspectionResult result)
        {
            var module = result.QualifiedSelection.QualifiedName;
            return string.Format(
                "{0}: {1} - {2}.{3}, line {4}",
                result.Severity,
                result.Name,
                module.Project.Name,
                module.Component.Name,
                result.QualifiedSelection.Selection.StartLine);
        }

        private int _issues;
        private void _inspector_IssuesFound(object sender, InspectorIssuesFoundEventArg e)
        {
            Interlocked.Add(ref _issues, e.Issues.Count);
            Control.Invoke((MethodInvoker) delegate
            {
                var newCount = _issues;
                Control.SetIssuesStatus(newCount);
            });
        }

        private void Control_QuickFix(object sender, QuickFixEventArgs e)
        {
            e.QuickFix(VBE);
            Control_RefreshCodeInspections(null, EventArgs.Empty);
        }

        public override void Show()
        {
            base.Show();
            Refresh();
        }

        private void Control_NavigateCodeIssue(object sender, NavigateCodeEventArgs e)
        {
            try
            {
                e.QualifiedName.Component.CodeModule.CodePane.SetSelection(e.Selection);
            }
            catch (COMException)
            {
                // gulp
            }
        }

        private void Control_RefreshCodeInspections(object sender, EventArgs e)
        {
            Refresh();
        }

        private async void Refresh()
        {
            Control.EnableRefresh(false);
            Control.Cursor = Cursors.WaitCursor;

            await Task.Run(() => RefreshAsync());

            if (_results != null)
            {
                Control.SetContent(_results.Select(item => new CodeInspectionResultGridViewItem(item))
                    .OrderBy(item => item.Component)
                    .ThenBy(item => item.Line));
            }

            Control.Cursor = Cursors.Default;
            Control.SetIssuesStatus(_issues, true);
            Control.EnableRefresh();
        }

        private async Task RefreshAsync()
        {
            try
            {
                var projectParseResult = await _inspector.Parse(VBE.ActiveVBProject, this);
                _results = await _inspector.FindIssuesAsync(projectParseResult);
            }
            catch (COMException)
            {
                // burp
            }
        }

        private void _inspector_Reset(object sender, EventArgs e)
        {
            _issues = 0;
            Control.Invoke((MethodInvoker) delegate
            {
                Control.SetIssuesStatus(_issues);
                Control.InspectionResults.Clear();
            });
        }
    }
}
