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

    // ==================== Task Definition ====================
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
            Name = name;
            Description = description;
            Deadline = deadline;
            ForgotTime = forgot_time;
            CreatTime = DateTime.Now;
        }

        public myTask(string name, string description, DateTime deadline, TimeSpan life_duration)
        {
            Name = name;
            Description = description;
            Deadline = deadline;
            ForgotTime = deadline + life_duration;
            CreatTime = DateTime.Now;
        }

        public myTask(string name, string description, DateTime deadline)
        {
            Name = name;
            Description = description;
            Deadline = deadline;
            ForgotTime = deadline + TimeSpan.FromDays(7);
            CreatTime = DateTime.Now;
        }
    }

    // ==================== Task Management ====================
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
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to deserialize json: {ex.Message}");
        }
    }

    public void Save(IWidgetContext context)
    {
        try
        {
            string filePath = Path.Combine(context.DataDirectory, "tasks.json");
            Directory.CreateDirectory(context.DataDirectory);
            File.WriteAllText(filePath, ToJson());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save tasks: {ex.Message}");
        }
    }

    public void Load(IWidgetContext context)
    {
        string filePath = Path.Combine(context.DataDirectory, "tasks.json");
        if (!File.Exists(filePath)) return;

        try
        {
            string json = File.ReadAllText(filePath);
            LoadFromJson(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load tasks: {ex.Message}");
        }
    }

    // ==================== Helpers & Calculation ====================
    private static string TaskScheduleTextShow(myTask task, TimeDisp.TimeDisplayMode mode)
    {
        TimeSpan timespan = task.Deadline - DateTime.Now;
        return TimeDisp.DispTimeSpan(timespan, mode);
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

    // ==================== UI State & Rendering ====================
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
            _listContainer = new StackPanel { Spacing = 6, Padding = new Thickness(0) };
        }

        _listContainer.Children.Clear();
        _uiReferences.Clear();
        _selectedTasks.Clear();

        // --- 1. 顶部工具栏布局（Sort、Add、Delete） ---
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string sortBtnText = _currentSortMode == TaskSortMode.ByCreateTime ? "🕒" : "⏳";
        var sortBtn = new Button
        {
            Content = sortBtnText,
            Margin = new Thickness(0, 0, 4, 0)
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
        addButton.Click += async (s, e) =>
        {
            await ShowTaskDialog(_listContainer.XamlRoot, null, mode, () =>
            {
                Save(context);
                BuildTaskUIList(mode, context);
            });
        };
        Grid.SetColumn(addButton, 1);
        headerGrid.Children.Add(addButton);

        var deleteBtn = new Button
        {
            Content = "Delete",
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
            Margin = new Thickness(4, 0, 0, 0),
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
                Content = $"Are you sure you want to delete {_selectedTasks.Count} selected task(s)?",
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
                Debug.WriteLine($"Dialog crash prevented: {ex.Message}");
            }
        };
        Grid.SetColumn(deleteBtn, 2);
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

        // --- 2. 静态页面骨架及列表构建 ---
        var taskRowsStack = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Border? _currentExpandedDescBorder = null;
        Border? _currentExpandedTaskItemBorder = null;

        // 颜色调配色板：调高高亮对比度
        var normalBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));             // 平时透明
        var hoverBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255));        // 悬停高亮
        var activeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 0, 120, 215));         // 展开高亮

        foreach (var task in tasks)
        {
            // 将外边距Margin减小到 0，缩减卡片Padding，极大化横向可用空间
            var taskItemBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 4, 2, 4), // 左右内边距压缩到 2px
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = normalBrush
            };

            var taskItemContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };

            var gridRow = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            gridRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textHeaderGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            textHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            textHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftText = new TextBlock
            {
                Text = task.Name,
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 15,
                Opacity = 0.9,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium
            };
            Grid.SetColumn(leftText, 0);
            textHeaderGrid.Children.Add(leftText);

            var rightText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 15,
                Opacity = 0.7
            };
            Grid.SetColumn(rightText, 1);
            textHeaderGrid.Children.Add(rightText);

            var taskContentStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };

            var progressBar = new ProgressBar
            {
                Height = 3,
                FlowDirection = FlowDirection.RightToLeft,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            taskContentStack.Children.Add(textHeaderGrid);
            taskContentStack.Children.Add(progressBar);

            Grid.SetColumn(taskContentStack, 0);
            gridRow.Children.Add(taskContentStack);

            // 复选框：压缩与左边的 Margin
            var selectCheck = new CheckBox
            {
                MinWidth = 0,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            selectCheck.Checked += (s, e) =>
            {
                _selectedTasks.Add(task);
                deleteBtn.IsEnabled = _selectedTasks.Count > 0;
            };
            selectCheck.Unchecked += (s, e) =>
            {
                _selectedTasks.Remove(task);
                deleteBtn.IsEnabled = _selectedTasks.Count > 0;
            };

            selectCheck.PointerPressed += (s, e) => e.Handled = true;
            selectCheck.PointerReleased += (s, e) => e.Handled = true;

            Grid.SetColumn(selectCheck, 1);
            gridRow.Children.Add(selectCheck);

            // Description 展开区域
            var descContainer = new Border
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 6, 6, 6),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 255, 255, 255)),
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var descGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            descGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            descGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var descText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(task.Description) ? "No description provided." : task.Description,
                FontSize = 13,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(descText, 0);
            descGrid.Children.Add(descText);

            var itemEditBtn = new Button
            {
                Content = "Edit",
                FontSize = 12,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            itemEditBtn.Click += async (s, e) =>
            {
                await ShowTaskDialog(_listContainer.XamlRoot, task, mode, () =>
                {
                    Save(context);
                    BuildTaskUIList(mode, context);
                });
            };
            itemEditBtn.PointerPressed += (s, e) => e.Handled = true;
            Grid.SetColumn(itemEditBtn, 1);
            descGrid.Children.Add(itemEditBtn);

            descContainer.Child = descGrid;

            taskItemContainer.Children.Add(gridRow);
            taskItemContainer.Children.Add(descContainer);
            taskItemBorder.Child = taskItemContainer;

            // ==================== 鼠标移动与悬停高亮逻辑 ====================
            taskItemBorder.PointerEntered += (s, e) =>
            {
                if (_currentExpandedTaskItemBorder != taskItemBorder)
                {
                    taskItemBorder.Background = hoverBrush;
                }
            };

            taskItemBorder.PointerExited += (s, e) =>
            {
                if (_currentExpandedTaskItemBorder != taskItemBorder)
                {
                    taskItemBorder.Background = normalBrush;
                }
            };

            taskItemBorder.PointerPressed += (s, e) =>
            {
                bool isCurrentAlreadyExpanded = descContainer.Visibility == Visibility.Visible;

                if (_currentExpandedDescBorder != null && _currentExpandedDescBorder != descContainer)
                {
                    _currentExpandedDescBorder.Visibility = Visibility.Collapsed;
                    if (_currentExpandedTaskItemBorder != null)
                    {
                        _currentExpandedTaskItemBorder.Background = normalBrush;
                    }
                }

                if (isCurrentAlreadyExpanded)
                {
                    descContainer.Visibility = Visibility.Collapsed;
                    taskItemBorder.Background = hoverBrush;
                    _currentExpandedDescBorder = null;
                    _currentExpandedTaskItemBorder = null;
                }
                else
                {
                    descContainer.Visibility = Visibility.Visible;
                    taskItemBorder.Background = activeBrush;
                    _currentExpandedDescBorder = descContainer;
                    _currentExpandedTaskItemBorder = taskItemBorder;
                }

                e.Handled = true;
            };

            taskRowsStack.Children.Add(taskItemBorder);
            _uiReferences[task] = (leftText, rightText, progressBar);
        }

        var listScrollViewer = new ScrollViewer
        {
            Height = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
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
        }
    }

    // ==================== Dialogs ====================
    private async Task<bool> ShowTaskDialog(XamlRoot xamlRoot, myTask? taskToEdit, TimeDisp.TimeDisplayMode mode, Action onSaveSuccess)
    {
        bool isEditMode = taskToEdit != null;

        var formContainer = new StackPanel { Spacing = 8, Width = 480 };

        var nameInput = new TextBox { Header = "Task Name*", PlaceholderText = "Enter task name...", Text = isEditMode ? taskToEdit!.Name : "" };
        formContainer.Children.Add(nameInput);

        var descInput = new TextBox { Header = "Description*", PlaceholderText = "Enter description...", Text = isEditMode ? taskToEdit!.Description : "" };
        formContainer.Children.Add(descInput);

        // ================= Deadline 部分 =================
        var deadlineHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(0, 4, 0, 0) };
        deadlineHeaderStack.Children.Add(new TextBlock { Text = "Deadline*", VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var deadlineSecCheck = new CheckBox { Content = "After any Seconds", VerticalAlignment = VerticalAlignment.Center};
        deadlineHeaderStack.Children.Add(deadlineSecCheck);
        formContainer.Children.Add(deadlineHeaderStack);

        var deadlineSecInput = new NumberBox { PlaceholderText = "Enter duration in seconds...", Visibility = Visibility.Collapsed };

        // 2. 将 Grid 改为 StackPanel 垂直排列，让 DatePicker 和 TimePicker 各自独占一行
        var deadlineNormalInput = new StackPanel { Spacing = 0, Visibility = Visibility.Visible };

        var deadlineDatePicker = new DatePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
        var deadlineTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch, ClockIdentifier = "24HourClock" };

        // 3. 允许下拉弹出层超出宿主窗口边界
        if (deadlineDatePicker.ContextFlyout != null) deadlineDatePicker.ContextFlyout.ShouldConstrainToRootBounds = false;
        if (deadlineTimePicker.ContextFlyout != null) deadlineTimePicker.ContextFlyout.ShouldConstrainToRootBounds = false;

        deadlineNormalInput.Children.Add(deadlineDatePicker);
        deadlineNormalInput.Children.Add(deadlineTimePicker);

        formContainer.Children.Add(deadlineSecInput);
        formContainer.Children.Add(deadlineNormalInput);

        // ================= Forgot Time 部分 =================
        var forgotHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(0, 4, 0, 0) };
        forgotHeaderStack.Children.Add(new TextBlock { Text = "Forgot Time", VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var forgotSecCheck = new CheckBox { Content = "After any Seconds", VerticalAlignment = VerticalAlignment.Center};
        forgotHeaderStack.Children.Add(forgotSecCheck);
        formContainer.Children.Add(forgotHeaderStack);

        var forgotSecInput = new NumberBox { PlaceholderText = "Enter duration in seconds...", Visibility = Visibility.Collapsed };

        // 同样将 Forgot Time 的时间选择器改为垂直排列
        var forgotNormalInput = new StackPanel { Spacing = 0, Visibility = Visibility.Visible };

        var forgotDatePicker = new DatePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
        var forgotTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch, ClockIdentifier = "24HourClock" };

        if (forgotDatePicker.ContextFlyout != null) forgotDatePicker.ContextFlyout.ShouldConstrainToRootBounds = false;
        if (forgotTimePicker.ContextFlyout != null) forgotTimePicker.ContextFlyout.ShouldConstrainToRootBounds = false;

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