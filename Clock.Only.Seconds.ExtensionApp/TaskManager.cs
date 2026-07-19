using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using WidBar.SDK;
using static TimeDisp;

public class TaskManager
{
    public TaskManager()
    {
    }

    // task definition
    public class myTask
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime Deadline { get; set; }
        public DateTime ForgotTime { get; set; }
        public DateTime CreatTime { get; private set; } = DateTime.Now;

        public myTask()
        {
            Name = string.Empty;
            Description = string.Empty;
            CreatTime = DateTime.Now;
        }

        public myTask(string name, string description, DateTime deadline, DateTime forgot_time)
        {
            this.Name = name;
            this.Description = description;
            this.Deadline = deadline;
            this.ForgotTime = forgot_time;
            CreatTime = DateTime.Now;
        }

        public myTask(string name, string description, DateTime deadline, TimeSpan life_duration)
        {
            this.Name = name;
            this.Description = description;
            this.Deadline = deadline;
            this.ForgotTime = deadline + life_duration;
            CreatTime = DateTime.Now;
        }

        public myTask(string name, string description, DateTime deadline)
        {
            this.Name = name;
            this.Description = description;
            this.Deadline = deadline;
            this.ForgotTime = deadline + TimeSpan.FromDays(7);
            CreatTime = DateTime.Now;
        }
    }

    // manager
    private readonly List<myTask> _tasks = new List<myTask>();
    private List<myTask> GetAllTasks() => _tasks;
    private void AddTask(myTask task) => _tasks.Add(task);
    private void RemoveTask(string name) => _tasks.RemoveAll(t => t.Name == name);
    private void RemoveTask(myTask task) => _tasks.Remove(task);

    private void Cleanup() => _tasks.RemoveAll(t => t.ForgotTime < DateTime.Now);

    public string ToJson() => JsonSerializer.Serialize(_tasks);

    public void LoadFromJson(string? json)
    {
        _tasks.Clear();
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var list = JsonSerializer.Deserialize<List<myTask>>(json) ?? new List<myTask>();
            _tasks.AddRange(list);
        }
        catch
        {
        }
    }

    public void Save(IWidgetContext context)
    {
        try
        {
            string filePath = Path.Combine(context.DataDirectory, "tasks.json");
            Directory.CreateDirectory(context.DataDirectory);
            File.WriteAllText(filePath, this.ToJson());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save tasks: {ex.Message}");
        }
    }

    public void Load(IWidgetContext context)
    {
        string filePath = Path.Combine(context.DataDirectory, "tasks.json");
        if (!File.Exists(filePath)) return;

        try
        {
            string json = File.ReadAllText(filePath);
            this.LoadFromJson(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load tasks: {ex.Message}");
        }
    }

    // display
    private static string TaskScheduleTextShow(myTask task, TimeDisp.TimeDisplayMode mode)
    {
        TimeSpan timespan = task.Deadline - DateTime.Now;
        string schedule = TimeDisp.DispTimeSpan(timespan, mode);
        return schedule;
    }

    private static (double progress, bool isOverdue) GetTaskProgress(myTask task)
    {
        DateTime now = DateTime.Now;
        TimeSpan timespan_ddl = task.Deadline - now;
        TimeSpan timespan_fgt = task.ForgotTime - now;

        if (timespan_ddl >= TimeSpan.Zero)
        {
            long totalTicks = (task.Deadline - task.CreatTime).Ticks;
            if (totalTicks <= 0) return (1.0, false);

            double remaining_rate = (double)timespan_ddl.Ticks / totalTicks;
            return (Math.Clamp(remaining_rate, 0.0, 1.0), false);
        }
        else if (timespan_fgt > TimeSpan.Zero)
        {
            long overdueDurationTicks = (task.ForgotTime - task.Deadline).Ticks;
            if (overdueDurationTicks <= 0) return (1.0, true);

            double remaining_rate = (double)timespan_fgt.Ticks / overdueDurationTicks;
            double forgot_rate = 1.0 - remaining_rate;
            return (Math.Clamp(forgot_rate, 0.0, 1.0), true);
        }
        else
        {
            return (1.0, true);
        }
    }

    private StackPanel? _listContainer;
    private readonly List<myTask> _selectedTasks = new();
    private readonly Dictionary<myTask, (TextBlock LeftTextControl, TextBlock RightTextControl, ProgressBar ProgressControl)> _uiReferences = new();

    private enum TaskSortMode
    {
        ByCreateTime,
        ByRemainingTime
    }
    private TaskSortMode _currentSortMode = TaskSortMode.ByRemainingTime;

    public UIElement BuildTaskUIList(TimeDisp.TimeDisplayMode mode, IWidgetContext context)
    {
        if (_listContainer == null)
        {
            _listContainer = new StackPanel { Spacing = 8 };
        }

        _listContainer.Children.Clear();
        _uiReferences.Clear();
        _selectedTasks.Clear();

        // --- 1. 顶部工具栏布局（Sort、Add、Edit、Delete 并排） ---
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string sortBtnText = _currentSortMode == TaskSortMode.ByCreateTime ? "🕒" : "⏳";
        var sortBtn = new Button
        {
            Content = sortBtnText,
            Margin = new Thickness(0, 0, 2, 0)
        };
        sortBtn.Click += (s, e) =>
        {
            _currentSortMode = _currentSortMode == TaskSortMode.ByCreateTime
                ? TaskSortMode.ByRemainingTime
                : TaskSortMode.ByCreateTime;
            BuildTaskUIList(mode, context);
        };
        Grid.SetColumn(sortBtn, 0);
        headerGrid.Children.Add(sortBtn);

        var addButton = new Button
        {
            Content = "+ Add new task",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        addButton.Click += async (s, e) => {
            await ShowTaskDialog(_listContainer.XamlRoot, null, mode, () => {
                Save(context);
                BuildTaskUIList(mode, context);
            });
        };
        Grid.SetColumn(addButton, 1);
        headerGrid.Children.Add(addButton);

        var editBtn = new Button
        {
            Content = "Edit",
            Margin = new Thickness(2, 0, 0, 0),
            IsEnabled = false
        };
        editBtn.Click += async (s, e) =>
        {
            if (_selectedTasks.Count != 1) return;
            var taskToEdit = _selectedTasks[0];
            await ShowTaskDialog(_listContainer.XamlRoot, taskToEdit, mode, () => {
                Save(context);
                BuildTaskUIList(mode, context);
            });
        };
        Grid.SetColumn(editBtn, 2);
        headerGrid.Children.Add(editBtn);

        var deleteBtn = new Button
        {
            Content = "Delete",
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
            Margin = new Thickness(2, 0, 0, 0),
            IsEnabled = false
        };
        deleteBtn.Click += async (s, e) =>
        {
            if (_selectedTasks.Count == 0) return;
            var currentRoot = deleteBtn.XamlRoot ?? _listContainer?.XamlRoot;
            if (currentRoot == null) return;

            var confirmDialog = new ContentDialog
            {
                Title = "Confirm Delete",
                Content = "Are you sure you want to delete the selected task(s)?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = currentRoot
            };

            try
            {
                var result = await confirmDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var tasksToRemove = new List<myTask>(_selectedTasks);
                    foreach (var task in tasksToRemove)
                    {
                        RemoveTask(task);
                    }
                    Save(context);
                    BuildTaskUIList(mode, context);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dialog crash prevented: {ex.Message}");
            }
        };
        Grid.SetColumn(deleteBtn, 3);
        headerGrid.Children.Add(deleteBtn);
        _listContainer.Children.Add(headerGrid);

        var tasks = GetSortedTasks();

        if (tasks.Count == 0)
        {
            _listContainer.Children.Add(new TextBlock
            {
                Text = "No Task",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 24,
                Opacity = 0.5,
                Margin = new Thickness(0, 10, 0, 10)
            });
            return _listContainer;
        }

        // --- 2. 静态页面骨架及选择器构建 ---
        var taskRowsStack = new StackPanel { Spacing = 0 };
        foreach (var task in tasks)
        {
            var gridRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            gridRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textHeaderGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            textHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            textHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 15,
                Opacity = 0.6
            };
            Grid.SetColumn(leftText, 0);
            textHeaderGrid.Children.Add(leftText);

            var rightText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 15,
                Opacity = 0.6
            };
            Grid.SetColumn(rightText, 1);
            textHeaderGrid.Children.Add(rightText);

            var taskContentStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };

            var progressBar = new ProgressBar
            {
                Height = 3,
                FlowDirection = FlowDirection.RightToLeft
            };

            taskContentStack.Children.Add(textHeaderGrid);
            taskContentStack.Children.Add(progressBar);

            Grid.SetColumn(taskContentStack, 0);
            gridRow.Children.Add(taskContentStack);

            var selectCheck = new CheckBox
            {
                MinWidth = 0,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            selectCheck.Checked += (s, e) => {
                _selectedTasks.Add(task);
                deleteBtn.IsEnabled = _selectedTasks.Count > 0;
                editBtn.IsEnabled = _selectedTasks.Count == 1;
            };
            selectCheck.Unchecked += (s, e) => {
                _selectedTasks.Remove(task);
                deleteBtn.IsEnabled = _selectedTasks.Count > 0;
                editBtn.IsEnabled = _selectedTasks.Count == 1;
            };

            Grid.SetColumn(selectCheck, 1);
            gridRow.Children.Add(selectCheck);

            taskRowsStack.Children.Add(gridRow);
            _uiReferences[task] = (leftText, rightText, progressBar);
        }

        var listScrollViewer = new ScrollViewer
        {
            Height = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = taskRowsStack
        };
        _listContainer.Children.Add(listScrollViewer);

        UpdateTaskUI(mode);
        return _listContainer;
    }

    public void UpdateTaskUI(TimeDisp.TimeDisplayMode mode)
    {
        foreach (var kp in _uiReferences)
        {
            var task = kp.Key;
            var (leftTextBlock, rightTextBlock, progressBar) = kp.Value;

            leftTextBlock.Text = task.Name;
            rightTextBlock.Text = TaskScheduleTextShow(task, mode);

            var (progress, isOverdue) = GetTaskProgress(task);
            if (!isOverdue)
            {
                progressBar.FlowDirection = FlowDirection.RightToLeft;
                progressBar.Value = progress * 100;
                if (progress > 0.75)
                {
                    progressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                }
                else if (progress > 0.50)
                {
                    progressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.YellowGreen);
                }
                else if (progress > 0.25)
                {
                    progressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Yellow);
                }
                else
                {
                    progressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                }
            }
            else
            {
                progressBar.FlowDirection = FlowDirection.LeftToRight;
                progressBar.Value = progress * 100;
                progressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                if (progress >= 1.0) progressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            }
        }/* END foreach (var kp in _uiReferences) */
    }

    private async Task<bool> ShowTaskDialog(XamlRoot xamlRoot, myTask? taskToEdit, TimeDisp.TimeDisplayMode mode, Action onSaveSuccess)
    {
        bool isEditMode = taskToEdit != null;

        var formContainer = new StackPanel { Spacing = 3, Width = 300 };

        var nameInput = new TextBox { Header = "Task Name* ", PlaceholderText = "Enter task name...", Text = isEditMode ? taskToEdit!.Name : "" };
        formContainer.Children.Add(nameInput);

        var descInput = new TextBox { Header = "Description* ", PlaceholderText = "Enter description...", Text = isEditMode ? taskToEdit!.Description : "" };
        formContainer.Children.Add(descInput);

        var deadlineHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        deadlineHeaderStack.Children.Add(new TextBlock { Text = "Deadline* ", VerticalAlignment = VerticalAlignment.Center });
        var deadlineSecCheck = new CheckBox { Content = "After any Seconds", VerticalAlignment = VerticalAlignment.Center };
        deadlineHeaderStack.Children.Add(deadlineSecCheck);
        formContainer.Children.Add(deadlineHeaderStack);
        var deadlineSecInput = new NumberBox { PlaceholderText = "Enter duration in seconds...", Visibility = Visibility.Collapsed };
        var deadlineNormalInput = new Grid { Visibility = Visibility.Visible };
        deadlineNormalInput.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        deadlineNormalInput.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var deadlineDatePicker = new DatePicker { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 4, 0) };
        var deadlineTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch, ClockIdentifier = "24HourClock" };
        Grid.SetColumn(deadlineDatePicker, 0);
        Grid.SetColumn(deadlineTimePicker, 1);
        deadlineNormalInput.Children.Add(deadlineDatePicker);
        deadlineNormalInput.Children.Add(deadlineTimePicker);
        formContainer.Children.Add(deadlineSecInput);
        formContainer.Children.Add(deadlineNormalInput);

        var forgotHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        forgotHeaderStack.Children.Add(new TextBlock { Text = "Forgot Time ", VerticalAlignment = VerticalAlignment.Center });
        var forgotSecCheck = new CheckBox { Content = "After any Seconds", VerticalAlignment = VerticalAlignment.Center };
        forgotHeaderStack.Children.Add(forgotSecCheck);
        formContainer.Children.Add(forgotHeaderStack);
        var forgotSecInput = new NumberBox { PlaceholderText = "Enter duration in seconds...", Visibility = Visibility.Collapsed };
        var forgotNormalInput = new Grid { Visibility = Visibility.Visible };
        forgotNormalInput.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        forgotNormalInput.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var forgotDatePicker = new DatePicker { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 4, 0) };
        var forgotTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch, ClockIdentifier = "24HourClock" };
        Grid.SetColumn(forgotDatePicker, 0);
        Grid.SetColumn(forgotTimePicker, 1);
        forgotNormalInput.Children.Add(forgotDatePicker);
        forgotNormalInput.Children.Add(forgotTimePicker);

        formContainer.Children.Add(forgotSecInput);
        formContainer.Children.Add(forgotNormalInput);

        if (isEditMode)
        {
            deadlineDatePicker.Date = taskToEdit!.Deadline.Date;
            deadlineTimePicker.Time = taskToEdit.Deadline.TimeOfDay;

            if (taskToEdit.ForgotTime != default)
            {
                forgotDatePicker.Date = taskToEdit.ForgotTime.Date;
                forgotTimePicker.Time = taskToEdit.ForgotTime.TimeOfDay;
            }
        }
        else
        {
            deadlineDatePicker.Date = DateTime.Now.Date;
            deadlineTimePicker.Time = DateTime.Now.TimeOfDay;
            forgotDatePicker.Date = DateTime.Now.Date;
            forgotTimePicker.Time = DateTime.Now.TimeOfDay;
        }

        deadlineSecCheck.Checked += (s, e) => { deadlineSecInput.Visibility = Visibility.Visible; deadlineNormalInput.Visibility = Visibility.Collapsed; };
        deadlineSecCheck.Unchecked += (s, e) => { deadlineSecInput.Visibility = Visibility.Collapsed; deadlineNormalInput.Visibility = Visibility.Visible; };
        forgotSecCheck.Checked += (s, e) => { forgotSecInput.Visibility = Visibility.Visible; forgotNormalInput.Visibility = Visibility.Collapsed; };
        forgotSecCheck.Unchecked += (s, e) => { forgotSecInput.Visibility = Visibility.Collapsed; forgotNormalInput.Visibility = Visibility.Visible; };

        var scrollWrapper = new ScrollViewer
        {
            Content = formContainer,
            MaxHeight = 300,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var dialog = new ContentDialog
        {
            Title = isEditMode ? "Edit Task" : "Create New Task",
            Content = scrollWrapper,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot
        };

        DateTime deadlineTime = DateTime.Now;
        DateTime forgotTime = DateTime.Now;
        bool hasForgot = false;

        Action validateInputs = () =>
        {
            bool isNameValid = !string.IsNullOrWhiteSpace(nameInput.Text);
            bool isDescValid = !string.IsNullOrWhiteSpace(descInput.Text);

            bool isDeadlineValid = deadlineSecCheck.IsChecked == true
                ? (!double.IsNaN(deadlineSecInput.Value) && deadlineSecInput.Value > 0)
                : true;

            if (!isNameValid || !isDescValid || !isDeadlineValid)
            {
                dialog.IsPrimaryButtonEnabled = false;
                return;
            }

            if (deadlineSecCheck.IsChecked == true)
            {
                deadlineTime = DateTime.Now.AddSeconds(deadlineSecInput.Value);
            }
            else
            {
                var date = deadlineDatePicker.Date.DateTime;
                var time = deadlineTimePicker.Time;
                deadlineTime = new DateTime(date.Year, date.Month, date.Day, time.Hours, time.Minutes, time.Seconds);
            }

            hasForgot = forgotSecCheck.IsChecked == true
                ? !double.IsNaN(forgotSecInput.Value)
                : true;

            bool isForgotValid = true;
            if (hasForgot)
            {
                if (forgotSecCheck.IsChecked == true)
                {
                    forgotTime = deadlineTime.AddSeconds(forgotSecInput.Value);
                }
                else
                {
                    var date = forgotDatePicker.Date.DateTime;
                    var time = forgotTimePicker.Time;
                    forgotTime = new DateTime(date.Year, date.Month, date.Day, time.Hours, time.Minutes, time.Seconds);
                }

                isForgotValid = forgotTime > deadlineTime;
            }

            dialog.IsPrimaryButtonEnabled = isForgotValid;
        };

        nameInput.TextChanged += (s, e) => validateInputs();
        descInput.TextChanged += (s, e) => validateInputs();
        deadlineSecInput.ValueChanged += (s, e) => validateInputs();
        deadlineDatePicker.DateChanged += (s, e) => validateInputs();
        deadlineTimePicker.TimeChanged += (s, e) => validateInputs();
        deadlineSecCheck.Checked += (s, e) => validateInputs();
        deadlineSecCheck.Unchecked += (s, e) => validateInputs();

        forgotSecInput.ValueChanged += (s, e) => validateInputs();
        forgotDatePicker.DateChanged += (s, e) => validateInputs();
        forgotTimePicker.TimeChanged += (s, e) => validateInputs();
        forgotSecCheck.Checked += (s, e) => validateInputs();
        forgotSecCheck.Unchecked += (s, e) => validateInputs();

        validateInputs();

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            if (isEditMode)
            {
                taskToEdit!.Name = nameInput.Text;
                taskToEdit.Description = descInput.Text;
                taskToEdit.Deadline = deadlineTime;
                taskToEdit.ForgotTime = hasForgot ? forgotTime : deadlineTime + TimeSpan.FromDays(7);
            }
            else
            {
                if (hasForgot)
                {
                    AddTask(new myTask(nameInput.Text, descInput.Text, deadlineTime, forgotTime));
                }
                else
                {
                    AddTask(new myTask(nameInput.Text, descInput.Text, deadlineTime));
                }
            }

            onSaveSuccess?.Invoke();
            return true;
        }

        return false;
    }

    public List<myTask> GetSortedTasks()
    {
        if (_currentSortMode == TaskSortMode.ByCreateTime)
        {
            return _tasks.OrderBy(t => t.CreatTime).ToList();
        }
        else
        {
            DateTime now = DateTime.Now;

            return _tasks
                .OrderBy(t => t.Deadline >= now ? 0 : (t.ForgotTime > now ? 1 : 2))
                .ThenBy(t => t.Deadline >= now ? t.Deadline.Ticks : 0)
                .ThenByDescending(t => t.Deadline < now && t.ForgotTime > now ? t.Deadline.Ticks : 0)
                .ThenBy(t => t.Deadline < now && t.ForgotTime <= now ? t.ForgotTime.Ticks : 0)
                .ToList();
        }
    }
}