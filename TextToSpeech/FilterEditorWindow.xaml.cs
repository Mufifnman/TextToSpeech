using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using PlainRules;
using TextToSpeech.Core.CustomFilters;

namespace TextToSpeech
{
    /// <summary>
    /// Small editor for a custom filter: name, menu group, an ordered list of plain-language rules
    /// (see the PlainRules library), lint messages and a live preview on sample text.
    /// All the rule logic lives in PlainRules; this window only edits and displays it.
    /// </summary>
    public partial class FilterEditorWindow : Window
    {
        private const int MaxSampleChars = 50_000;

        private readonly CustomFilterLibrary library;
        private readonly CustomFilterEntry? existing;
        private readonly ObservableCollection<RuleRow> rows = new();
        private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly HashSet<string> otherNames;
        private int refreshVersion;

        /// <summary>True when a filter file was written (or deleted) and the menu should be rebuilt.</summary>
        public bool Changed { get; private set; }

        internal FilterEditorWindow(CustomFilterLibrary library, CustomFilterEntry? existing, string sample)
        {
            InitializeComponent();
            this.library = library;
            this.existing = existing;
            otherNames = new HashSet<string>(
                FilterRegistry.All.Where(f => f.Id != existing?.Id).Select(f => f.Label), StringComparer.OrdinalIgnoreCase);

            rulesList.ItemsSource = rows;
            sampleBox.Text = sample.Length > MaxSampleChars ? sample.Substring(0, MaxSampleChars) : sample;

            if (existing?.Set is { } set)
            {
                Title = "Edit filter";
                nameBox.Text = set.Name;
                groupBox.Text = set.Group ?? "";
                foreach (var rule in set.Rules) AddRow(RuleRow.FromRule(rule));
                deleteButton.Visibility = Visibility.Visible;
                fileText.Text = existing.FilePath;
            }
            else
            {
                Title = "New filter";
                groupBox.Text = CustomFilterManager.DefaultGroup;
                AddRow(new RuleRow());
                fileText.Text = "Saved in " + library.Folder;
            }

            nameBox.TextChanged += (_, _) => ScheduleRefresh();
            sampleBox.TextChanged += (_, _) => ScheduleRefresh();
            refreshTimer.Tick += async (_, _) => { refreshTimer.Stop(); await RefreshAsync(); };
            Loaded += (_, _) => { _ = RefreshAsync(); nameBox.Focus(); };
            Closed += (_, _) => refreshTimer.Stop();
        }

        // ---- rows ----

        private void AddRow(RuleRow row)
        {
            row.PropertyChanged += (_, _) => ScheduleRefresh();
            rows.Add(row);
        }

        private static RuleRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as RuleRow;

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            AddRow(new RuleRow());
            ScheduleRefresh();
        }

        private void AddPreset_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            foreach (var preset in Presets.All)
            {
                var item = new MenuItem { Header = preset.Name, ToolTip = preset.Description + "\n" + preset.Example, Tag = preset.Id };
                item.Click += (_, _) =>
                {
                    AddRow(new RuleRow { Kind = RuleKind.Preset, PresetId = preset.Id });
                    ScheduleRefresh();
                };
                menu.Items.Add(item);
            }
            PopupMenus.Open(menu, (UIElement)sender, PlacementMode.Bottom);
        }

        private void KindButton_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is not { } row) return;
            var menu = new ContextMenu();
            foreach (var (kind, label) in RuleRow.KindLabels)
            {
                var item = new MenuItem { Header = label, IsChecked = row.Kind == kind };
                var chosen = kind;
                item.Click += (_, _) => row.SetKind(chosen);
                menu.Items.Add(item);
            }
            PopupMenus.Open(menu, (UIElement)sender, PlacementMode.Bottom);
        }

        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is not { } row) return;
            var menu = new ContextMenu();
            foreach (var preset in Presets.All)
            {
                var item = new MenuItem { Header = preset.Name, ToolTip = preset.Description + "\n" + preset.Example, IsChecked = row.PresetId == preset.Id };
                var id = preset.Id;
                item.Click += (_, _) => row.PresetId = id;
                menu.Items.Add(item);
            }
            PopupMenus.Open(menu, (UIElement)sender, PlacementMode.Bottom);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(RowOf(sender), -1);
        private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(RowOf(sender), 1);

        private void Move(RuleRow? row, int delta)
        {
            if (row == null) return;
            int index = rows.IndexOf(row), target = index + delta;
            if (index < 0 || target < 0 || target >= rows.Count) return;
            rows.Move(index, target);
            ScheduleRefresh();
        }

        private void RemoveRule_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is { } row) rows.Remove(row);
            ScheduleRefresh();
        }

        private void GroupPick_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var groups = FilterRegistry.Groups.Append(CustomFilterManager.DefaultGroup).Distinct().ToList();
            foreach (var group in groups)
            {
                var item = new MenuItem { Header = group };
                var chosen = group;
                item.Click += (_, _) => groupBox.Text = chosen;
                menu.Items.Add(item);
            }
            PopupMenus.Open(menu, (UIElement)sender, PlacementMode.Bottom);
        }

        // ---- lint and preview ----

        private RuleSet BuildSet() => new()
        {
            Name = nameBox.Text.Trim(),
            Group = string.IsNullOrWhiteSpace(groupBox.Text) ? null : groupBox.Text.Trim(),
            Description = existing?.Set?.Description,
            Rules = rows.Select(r => r.ToRule()).ToList(),
        };

        private void ScheduleRefresh()
        {
            refreshTimer.Stop();
            refreshTimer.Start();
        }

        /// <summary>Runs lint and preview off the UI thread (a bad rule can take up to its timeout) and shows the newest result only.</summary>
        private async Task RefreshAsync()
        {
            int version = ++refreshVersion;
            var set = BuildSet();
            string sample = sampleBox.Text;
            var names = otherNames;
            var (issues, preview) = await Task.Run(() =>
                (RuleLinter.Check(set, sample.Length == 0 ? null : sample, names), RuleEngine.Preview(set, sample)));
            if (version != refreshVersion) return;   // a newer edit superseded this one
            ShowLint(issues);
            resultBox.Text = preview.Output;
            saveButton.IsEnabled = RuleLinter.IsValid(issues);
        }

        private void ShowLint(IReadOnlyList<LintIssue> issues)
        {
            lintText.Inlines.Clear();
            if (issues.Count == 0)
            {
                lintText.Inlines.Add(new Run("No problems found.") { Foreground = (Brush)FindResource("ThemeSubtle") });
                return;
            }
            foreach (var issue in issues.OrderByDescending(i => i.Severity))
            {
                (string mark, Brush brush) = issue.Severity switch
                {
                    LintSeverity.Error => ("⛔ ", Brushes.IndianRed),
                    LintSeverity.Warning => ("⚠ ", Brushes.DarkOrange),
                    _ => ("ℹ ", (Brush)FindResource("ThemeSubtle")),
                };
                lintText.Inlines.Add(new Run(mark + issue + "\n") { Foreground = brush });
            }
        }

        // ---- save / delete ----

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var set = BuildSet();
            var issues = RuleLinter.Check(set, null, otherNames);
            if (!RuleLinter.IsValid(issues))
            {
                ShowLint(issues);
                return;
            }
            try
            {
                library.Save(set, existing?.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The filter could not be saved: " + ex.Message, "Filter editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Changed = true;
            DialogResult = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (existing == null) return;
            var answer = MessageBox.Show(this, $"Delete the filter “{existing.DisplayName}” and its file?", "Filter editor",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            try
            {
                library.Delete(existing.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The filter could not be deleted: " + ex.Message, "Filter editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Changed = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>One editable rule row. Converts to and from a PlainRules <see cref="Rule"/>.</summary>
        internal sealed class RuleRow : INotifyPropertyChanged
        {
            public static readonly (RuleKind Kind, string Label)[] KindLabels =
            {
                (RuleKind.RemoveText, "Remove text"),
                (RuleKind.Replace, "Replace text"),
                (RuleKind.RemoveLinesStarting, "Remove lines starting with"),
                (RuleKind.RemoveLinesContaining, "Remove lines containing"),
                (RuleKind.RemoveBetween, "Remove from ... to ..."),
                (RuleKind.RemoveWords, "Remove words"),
                (RuleKind.Preset, "Common cleanup"),
                (RuleKind.Regex, "Advanced: regex"),
            };

            private RuleKind kind = RuleKind.RemoveText;
            private string value = "", extra = "", presetId = "";
            private bool ignoreCase, wholeWords, disabled;   // disabled is kept from hand-edited files, not editable here

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            public RuleKind Kind { get => kind; set { kind = value; Raise(); } }
            public string Value { get => value; set { this.value = value; Raise(); } }
            public string Extra { get => extra; set { extra = value; Raise(); } }
            public string PresetId { get => presetId; set { presetId = value; Raise(); } }
            public bool IgnoreCase { get => ignoreCase; set { ignoreCase = value; Raise(); } }
            public bool WholeWords { get => wholeWords; set { wholeWords = value; Raise(); } }

            /// <summary>Switch kind and refresh everything that depends on it.</summary>
            public void SetKind(RuleKind newKind)
            {
                kind = newKind;
                if (newKind == RuleKind.Preset && Presets.Find(presetId) == null)
                    presetId = Presets.All[0].Id;
                Raise(string.Empty);   // all properties changed
            }

            public string KindLabel => KindLabels.First(k => k.Kind == kind).Label;
            public bool IsPreset => kind == RuleKind.Preset;
            public bool ShowValueBox => kind != RuleKind.Preset;
            public bool ShowExtra => kind is RuleKind.Replace or RuleKind.RemoveBetween or RuleKind.Regex;
            public bool ShowCaseOptions => kind != RuleKind.Preset;
            public bool ShowWholeWords => kind is RuleKind.RemoveText or RuleKind.Replace or RuleKind.RemoveLinesContaining;
            public string PresetLabel => Presets.Find(presetId)?.Name ?? "Choose a cleanup...";

            public string ValueHint => kind switch
            {
                RuleKind.RemoveText => "The text to remove",
                RuleKind.Replace => "The text to find",
                RuleKind.RemoveLinesStarting => "Lines starting with this are removed",
                RuleKind.RemoveLinesContaining => "Lines containing this are removed",
                RuleKind.RemoveBetween => "Start marker (removed together with the end marker)",
                RuleKind.RemoveWords => "Words to remove, separated by commas",
                RuleKind.Regex => "Regex pattern (advanced)",
                _ => "",
            };

            public string ExtraHint => kind switch
            {
                RuleKind.Replace => "Replace with (empty removes it)",
                RuleKind.RemoveBetween => "End marker",
                RuleKind.Regex => "Replacement, $1 is the first group (empty removes)",
                _ => "",
            };

            public Rule ToRule() => new()
            {
                Kind = kind,
                Value = kind == RuleKind.Preset ? presetId : value,
                With = kind is RuleKind.Replace or RuleKind.Regex ? extra : null,
                To = kind == RuleKind.RemoveBetween ? extra : null,
                IgnoreCase = ShowCaseOptions && ignoreCase,
                WholeWords = ShowWholeWords && wholeWords,
                Disabled = disabled,
            };

            public static RuleRow FromRule(Rule rule) => new()
            {
                kind = rule.Kind,
                value = rule.Kind == RuleKind.Preset ? "" : rule.Value,
                presetId = rule.Kind == RuleKind.Preset ? rule.Value : "",
                extra = rule.With ?? rule.To ?? "",
                ignoreCase = rule.IgnoreCase,
                wholeWords = rule.WholeWords,
                disabled = rule.Disabled,
            };
        }
    }
}
