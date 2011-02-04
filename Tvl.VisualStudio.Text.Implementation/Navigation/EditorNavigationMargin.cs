﻿namespace Tvl.VisualStudio.Text.Navigation.Implementation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Input;
    using Microsoft.VisualStudio.Text;
    using Microsoft.VisualStudio.Text.Editor;
    using Tvl.Events;

    internal class EditorNavigationMargin : IWpfTextViewMargin
    {
        private readonly IWpfTextView _wpfTextView;
        private readonly IEnumerable<IEditorNavigationSource> _sources;
        private readonly IEditorNavigationTypeRegistryService _editorNavigationTypeRegistryService;

        private readonly UniformGrid _container;
        private readonly Tuple<IEditorNavigationType, ComboBox>[] _navigationControls;

        public EditorNavigationMargin(IWpfTextView wpfTextView, IEnumerable<IEditorNavigationSource> sources, IEditorNavigationTypeRegistryService editorNavigationTypeRegistryService)
        {
            this._wpfTextView = wpfTextView;
            this._sources = sources;
            this._editorNavigationTypeRegistryService = editorNavigationTypeRegistryService;

            _navigationControls =
                this._sources
                .SelectMany(source => source.GetNavigationTypes())
                .Distinct()
                //.OrderBy(...)
                .Select(type => Tuple.Create(type, default(ComboBox)))
                .ToArray();

            if (this._navigationControls.Length == 0)
            {
                this._container =
                    new UniformGrid()
                    {
                        Visibility = Visibility.Collapsed
                    };

                return;
            }

            this._container = new UniformGrid()
            {
                Columns = _navigationControls.Length,
                Rows = 1
            };

            _navigationControls = Array.ConvertAll(_navigationControls,
                pair =>
                {
                    ComboBox comboBox =
                        new EditorNavigationComboBox()
                        {
                            Cursor = Cursors.Arrow,
                            ToolTip = new ToolTip()
                            {
                                Content = pair.Item1.Definition.DisplayName
                            }
                        };

                    comboBox.DropDownOpened += OnDropDownOpened;
                    comboBox.SelectionChanged += OnSelectionChanged;
                    return Tuple.Create(pair.Item1, comboBox);
                });

            foreach (var controlPair in _navigationControls)
            {
                this._container.Children.Add(controlPair.Item2);
            }

            this._wpfTextView.Caret.PositionChanged += OnCaretPositionChanged;
            foreach (var source in this._sources)
            {
                source.NavigationTargetsChanged += WeakEvents.AsWeak(OnNavigationTargetsChanged, eh => source.NavigationTargetsChanged -= eh);
                UpdateNavigationTargets(source);
            }

        }

        public bool Disposed
        {
            get;
            private set;
        }

        public bool IsDisposing
        {
            get;
            private set;
        }

        public bool Updating
        {
            get;
            private set;
        }

        public FrameworkElement VisualElement
        {
            get
            {
                return _container;
            }
        }

        public bool Enabled
        {
            get
            {
                return false;
            }
        }

        public ITextViewMargin GetTextViewMargin(string marginName)
        {
            if (marginName == "Editor Navigation Margin")
                return this;

            return null;
        }

        public double MarginSize
        {
            get
            {
                return VisualElement.ActualHeight;
            }
        }

        public void Dispose()
        {
            if (IsDisposing)
                throw new InvalidOperationException();

            try
            {
                IsDisposing = true;
                Dispose(true);
            }
            finally
            {
                IsDisposing = false;
            }

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                Disposed = true;
            }
        }

        private void UpdateNavigationTargets(IEditorNavigationSource source)
        {
            lock (this)
            {
                if (Updating)
                    return;
            }

            try
            {
                Updating = true;
                var targets = source.GetNavigationTargets().ToArray();
                Action action = () => UpdateNavigationTargets(targets);
                VisualElement.Dispatcher.Invoke(action);
            }
            finally
            {
                Updating = false;
            }
        }

        private void UpdateNavigationTargets(IEnumerable<IEditorNavigationTarget> targets)
        {
            foreach (var group in targets.GroupBy(target => target.EditorNavigationType))
            {
                var navigationControl = this._navigationControls.FirstOrDefault(control => control.Item1 == group.Key);
                if (navigationControl == null)
                    continue;

                var combo = navigationControl.Item2;
                combo.Items.Clear();
                foreach (var item in group.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
                    combo.Items.Add(item);
            }

            UpdateSelectedNavigationTargets();
        }

        private void OnNavigationTargetsChanged(object sender, EventArgs e)
        {
            IEditorNavigationSource source = (IEditorNavigationSource)sender;
            UpdateNavigationTargets(source);
        }

        private void UpdateSelectedNavigationTargets()
        {
            if (Thread.CurrentThread != _container.Dispatcher.Thread)
            {
                _container.Dispatcher.Invoke((Action)UpdateSelectedNavigationTargets);
                return;
            }

            var currentPosition = _wpfTextView.Caret.Position.BufferPosition;

            foreach (var control in this._navigationControls)
            {
                if (!control.Item2.HasItems)
                    continue;

                var oldSelectedItem = (IEditorNavigationTarget)control.Item2.SelectedItem;

                var positionOrderedItems = control.Item2.Items.Cast<IEditorNavigationTarget>().OrderBy(i => i.Span.Start).ToArray();
                var newSelectedItem =
                    positionOrderedItems
                    .LastOrDefault(i => i.Span.TranslateTo(currentPosition.Snapshot, SpanTrackingMode.EdgeInclusive).Contains(currentPosition));

                if (newSelectedItem == null)
                {
                    // select the first item starting after the current position
                    newSelectedItem =
                        positionOrderedItems.FirstOrDefault(i => i.Span.TranslateTo(currentPosition.Snapshot, SpanTrackingMode.EdgeInclusive).Start > currentPosition);
                }

                if (newSelectedItem == null)
                {
                    // select the last item ending before the current position
                    newSelectedItem =
                        positionOrderedItems.LastOrDefault(i => i.Span.TranslateTo(currentPosition.Snapshot, SpanTrackingMode.EdgeInclusive).End < currentPosition);
                }

                if (newSelectedItem == null)
                {
                    // select the first item
                    newSelectedItem = (IEditorNavigationTarget)control.Item2.Items[0];
                }

                bool wasUpdating = Updating;
                try
                {
                    Updating = true;
                    control.Item2.SelectedItem = newSelectedItem;
                }
                finally
                {
                    Updating = wasUpdating;
                }
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            UpdateSelectedNavigationTargets();
        }

        private void OnDropDownOpened(object sender, EventArgs e)
        {
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Updating)
                return;

            if (e.AddedItems.Count > 0)
            {
                IEditorNavigationTarget target = e.AddedItems[0] as IEditorNavigationTarget;
                if (target != null)
                {
                    var seek = target.Seek.Snapshot == null ? target.Span : target.Seek;
                    _wpfTextView.Caret.MoveTo(seek.Start);
                    _wpfTextView.Selection.Select(seek, false);
                    _wpfTextView.ViewScroller.EnsureSpanVisible(target.Seek);
                    Keyboard.Focus(_wpfTextView.VisualElement);
                }
            }
        }
    }
}
