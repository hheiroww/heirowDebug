Imports System.Collections.ObjectModel
Imports System.Collections
Imports System.ComponentModel
Imports System.Diagnostics.Eventing.Reader
Imports System.Net.Security
Imports System.Reflection
Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Controls.Primitives
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Windows.Shapes
Imports System.Xml.Serialization
Imports ControlzEx.Theming
Imports heirowDebug.WPF.Collections
Imports heirowDebug.WPF.Values
Imports MahApps.Metro.Controls
Imports SocketJack
Imports SocketJack.Serialization
Imports System.Linq
Imports System.Globalization

Public Class DebugWindow
    Inherits MetroWindow

    Private Enum WatchTargetKind
        WindowInstance
        StaticObject
        StaticType
    End Enum

    Private NotInheritable Class StaticObjectTarget
        Public Sub New(ownerType As Type, member As MemberInfo, declaredType As Type)
            Me.OwnerType = ownerType
            Me.Member = member
            Me.DeclaredType = declaredType
        End Sub

        Public ReadOnly Property OwnerType As Type
        Public ReadOnly Property Member As MemberInfo
        Public ReadOnly Property DeclaredType As Type
        Public ReadOnly Property SourceName As String
            Get
                Return If(OwnerType.FullName, OwnerType.Name) & "." & Member.Name
            End Get
        End Property

        Public Function Resolve() As Object
            If TypeOf Member Is FieldInfo Then Return DirectCast(Member, FieldInfo).GetValue(Nothing)
            Return DirectCast(Member, PropertyInfo).GetValue(Nothing)
        End Function
    End Class

    Private NotInheritable Class WatchTargetOption
        Public Sub New(kind As WatchTargetKind, displayText As String, searchText As String, target As Object)
            Me.Kind = kind
            Me.DisplayText = displayText
            Me.SearchText = searchText
            Me.Target = target
        End Sub

        Public ReadOnly Property Kind As WatchTargetKind
        Public ReadOnly Property DisplayText As String
        Public ReadOnly Property SearchText As String
        Public ReadOnly Property Target As Object

        Public Overrides Function ToString() As String
            Return DisplayText
        End Function
    End Class

    Private ReadOnly AllAvailableTargets As New List(Of WatchTargetOption)()
    Private ReadOnly FilteredAvailableTargets As New ObservableCollection(Of WatchTargetOption)()
    Private ReadOnly TargetRefreshTimer As New System.Windows.Threading.DispatcherTimer()
    Private ReadOnly RecursiveTargets As Boolean =
        Not String.Equals(Environment.GetEnvironmentVariable("heirowDebug_RECURSIVE"), "0", StringComparison.Ordinal)
    Private IsRefreshingTargets As Boolean
    Private LastHostWindowCount As Integer = -1
    Private LastAssemblyCount As Integer = -1

#Region "UI"

    Private isUserChangingLower As Boolean = False,
            isUserChangingUpper As Boolean = False,
            Range As Integer

    Public Sub New()

        ' This call is required by the designer.
        InitializeComponent()

        ' Add any initialization after the InitializeComponent() call.

        ThemeManager.Current.ChangeTheme(Me, "Dark.Red")
        AvailableTargetsComboBox.ItemsSource = FilteredAvailableTargets
        TargetRefreshTimer.Interval = TimeSpan.FromSeconds(1)
        AddHandler TargetRefreshTimer.Tick, AddressOf TargetRefreshTimer_Tick
        SetRange()
    End Sub


    Private Sub SetRange()
        If isUserChangingLower Or isUserChangingUpper Then Return
        Range = If(isAtBeginning And isAtEnd, MaxRange, Math.Min(Upper - Lower, MaxRange))
    End Sub

    Private Async Sub DebugWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight

        Enabled = ToggleEnabled.IsOn
        Await RefreshAvailableTargetsAsync()
        TargetRefreshTimer.Start()
    End Sub

    Private Sub Watchers_SelectedItemChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Object)) Handles Watchers.SelectedItemChanged
        If e.NewValue Is Nothing OrElse e.NewValue.GetType IsNot GetType(TreeViewItem) Then Return
        Dim TreeItem As TreeViewItem = DirectCast(e.NewValue, TreeViewItem)
        SelectedNestedMember = TryCast(TreeItem.Tag, NestedMember)
        NestedSampleTimer.Stop()
        If SelectedNestedMember IsNot Nothing Then
            _selectedValueGuid = Nothing
            ValueActions.Visibility = Visibility.Collapsed
            ClearPoints()
            CurrentTimeline = SelectedNestedMember.Timeline
            KeepAtEnd = True
            _lastRenderSignature = ""
            NestedSampleTimer.Start()
            SampleNestedMember(Nothing, EventArgs.Empty)
            Return
        End If
        If Not SubItemSelected(TreeItem) Then
            Dim tag As String = TryCast(TreeItem.Tag, String)
            Dim parentItem As TreeViewItem = TryCast(TreeItem.Parent, TreeViewItem)
            While tag Is Nothing AndAlso parentItem IsNot Nothing
                tag = TryCast(parentItem.Tag, String)
                parentItem = TryCast(parentItem.Parent, TreeViewItem)
            End While

            If tag IsNot Nothing AndAlso ValueOwnerLookup.ContainsKey(tag) Then
                Dim info = ValueOwnerLookup(tag)
                Dim val = info.Item1
                Dim watcher = info.Item2
                _selectedValueGuid = tag
                ShowValueActions(val, watcher)
                SetTimeline(tag)
                Return
            End If

            _selectedValueGuid = Nothing
            HideAddWatchButton()
            SetTimeline(tag)
        End If
    End Sub

    Private Sub DebugWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        NestedSampleTimer.Stop()
        SelectedNestedMember = Nothing
        TargetRefreshTimer.Stop()
        Enabled = False
        SyncLock BackgroundWorkerGate
            If BackgroundWorker IsNot Nothing Then BackgroundWorker.Dispose()
            BackgroundWorker = Nothing
        End SyncLock
        Dim watchersToDispose As List(Of DebugWatcher)
        SyncLock DebugWatcher.Watchers
            watchersToDispose = DebugWatcher.Watchers.Values.ToList()
        End SyncLock
        For Each w In watchersToDispose
            w.Dispose()
        Next
        SyncLock DebugWatcher.Watchers
            DebugWatcher.Watchers.Clear()
        End SyncLock
    End Sub

    Private Sub Slider_Loaded(sender As Object, e As RoutedEventArgs) Handles Slider.Loaded
        Slider_LowerValueChanged(Nothing, Nothing)
    End Sub

    Private Sub Plot_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles Plot.SizeChanged
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight
        ClearPoints()
    End Sub

    Private Sub ToggleEnabled_Toggled(sender As Object, e As RoutedEventArgs) Handles ToggleEnabled.Toggled
        If Not IsLoaded Then Return
        Enabled = ToggleEnabled.IsOn
    End Sub

    Private Sub Slider_LowerValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles Slider.LowerValueChanged
        If IsMaxWidth Then
            If e IsNot Nothing Then e.Handled = True
            Range = MaxRange
            Lower = Upper - MaxRange
        End If
        _Lower = Slider.LowerValue
        _lastRenderSignature = ""
        If IsLoaded AndAlso CurrentTimeline IsNot Nothing Then DrawTimeline()
    End Sub

    Private Sub Slider_UpperValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles Slider.UpperValueChanged
        If IsMaxWidth Then
            If e IsNot Nothing Then e.Handled = True
            Lower = Upper - Range
        End If
        _Upper = Slider.UpperValue
        _lastRenderSignature = ""
        If IsLoaded AndAlso CurrentTimeline IsNot Nothing Then DrawTimeline()
    End Sub

    Private Sub Slider_LowerThumbDragStarted(sender As Object, e As DragStartedEventArgs) Handles Slider.LowerThumbDragStarted
        SetRange()
        isUserChangingLower = True
        KeepAtEnd = False
    End Sub

    Private Sub Slider_LowerThumbDragCompleted(sender As Object, e As DragCompletedEventArgs) Handles Slider.LowerThumbDragCompleted
        isUserChangingLower = False
        SetRange()
    End Sub

    Private Sub Slider_UpperThumbDragStarted(sender As Object, e As DragStartedEventArgs) Handles Slider.UpperThumbDragStarted
        SetRange()
        isUserChangingUpper = True
        KeepAtEnd = False
    End Sub

    Private Sub Slider_UpperThumbDragCompleted(sender As Object, e As DragCompletedEventArgs) Handles Slider.UpperThumbDragCompleted
        isUserChangingUpper = False
        SetRange()
    End Sub

    Private Sub Slider_LowerThumbDragDelta(sender As Object, e As DragDeltaEventArgs) Handles Slider.LowerThumbDragDelta
        SetRange()
    End Sub

    Private Sub Slider_UpperThumbDragDelta(sender As Object, e As DragDeltaEventArgs) Handles Slider.UpperThumbDragDelta
        SetRange()
    End Sub

    Private Sub Slider_CentralThumbDragCompleted(sender As Object, e As DragCompletedEventArgs) Handles Slider.CentralThumbDragCompleted
        isUserChangingUpper = False
        isUserChangingLower = False
        SetRange()
    End Sub

    Private Sub Slider_CentralThumbDragStarted(sender As Object, e As DragStartedEventArgs) Handles Slider.CentralThumbDragStarted
        SetRange()
        isUserChangingUpper = True
        isUserChangingLower = True
        KeepAtEnd = False
    End Sub

    Private Async Sub RefreshTargetsButton_Click(sender As Object, e As RoutedEventArgs)
        Await RefreshAvailableTargetsAsync()
    End Sub

    Private Async Sub WatchTargetMenuButton_Click(sender As Object, e As RoutedEventArgs)
        WatchTargetPopup.IsOpen = True
        Await RefreshAvailableTargetsAsync()
        TargetFilterTextBox.Focus()
    End Sub

    Private Async Sub AddTargetButton_Click(sender As Object, e As RoutedEventArgs)
        Dim selected = TryCast(AvailableTargetsComboBox.SelectedItem, WatchTargetOption)
        If selected Is Nothing Then
            TargetStatusText.Text = "Select a window or static type first."
            Return
        End If

        Dim watcher As DebugWatcher = Nothing
        Select Case selected.Kind
            Case WatchTargetKind.WindowInstance
                watcher = DebugWatcher.WatchWindow(TryCast(selected.Target, Window), RecursiveTargets)
            Case WatchTargetKind.StaticObject
                Dim staticTarget = TryCast(selected.Target, StaticObjectTarget)
                If staticTarget IsNot Nothing Then
                    Try
                        Dim instance = staticTarget.Resolve()
                        If instance Is Nothing Then
                            TargetStatusText.Text = staticTarget.SourceName & " is currently null; no object has been instantiated there."
                            Return
                        End If
                        watcher = DebugWatcher.WatchStaticObject(instance, staticTarget.SourceName, RecursiveTargets)
                    Catch ex As Exception
                        TargetStatusText.Text = "Could not read " & staticTarget.SourceName & ": " & ex.GetBaseException().Message
                        Return
                    End Try
                End If
            Case WatchTargetKind.StaticType
                watcher = DebugWatcher.WatchStaticType(TryCast(selected.Target, Type), RecursiveTargets)
        End Select

        If watcher Is Nothing Then
            TargetStatusText.Text = "That target is no longer available. Refresh the list and try again."
            Return
        End If

        If Enabled Then watcher.StartSession()
        TargetStatusText.Text = "Watching " & watcher.Name
        Await RefreshAvailableTargetsAsync(selected.Target)
        WatchTargetPopup.IsOpen = False
    End Sub

    Private Async Sub AddAllWindowsButton_Click(sender As Object, e As RoutedEventArgs)
        Dim added = EnsureAllWindowWatchers()
        TargetStatusText.Text = If(added = 0, "Every instantiated application window is already watched.",
                                   "Added " & added & " window watcher" & If(added = 1, ".", "s."))
        Await RefreshAvailableTargetsAsync()
    End Sub

    Private Sub TargetFilterTextBox_TextChanged(sender As Object, e As TextChangedEventArgs)
        ApplyTargetFilter()
    End Sub

    Private Async Sub TargetRefreshTimer_Tick(sender As Object, e As EventArgs)
        Dim hostApplication As System.Windows.Application = System.Windows.Application.Current
        If hostApplication Is Nothing Then Return

        Dim hostWindows = hostApplication.Windows.Cast(Of Window)().
            Count(Function(window) Not Object.ReferenceEquals(window, Me) AndAlso window.GetType().Assembly IsNot Me.GetType().Assembly)
        Dim assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length
        If hostWindows <> LastHostWindowCount OrElse assemblyCount <> LastAssemblyCount Then
            Await RefreshAvailableTargetsAsync()
        End If
    End Sub

    Private Function EnsureAllWindowWatchers() As Integer
        Dim hostApplication As System.Windows.Application = System.Windows.Application.Current
        If hostApplication Is Nothing Then Return 0

        Dim added As Integer
        Dim windows = hostApplication.Windows.Cast(Of Window)().ToArray()
        For Each window In windows
            If Object.ReferenceEquals(window, Me) OrElse window.GetType().Assembly Is Me.GetType().Assembly Then Continue For

            Dim alreadyWatching As Boolean
            SyncLock DebugWatcher.Watchers
                alreadyWatching = DebugWatcher.Watchers.Values.Any(
                    Function(candidate) Not candidate.isChild AndAlso Not candidate.IsStaticRoot AndAlso
                                        Object.ReferenceEquals(candidate.AttachedObject, window))
            End SyncLock
            If alreadyWatching Then Continue For

            Dim watcher = DebugWatcher.WatchWindow(window, RecursiveTargets)
            If watcher IsNot Nothing Then
                If Enabled Then watcher.StartSession()
                added += 1
            End If
        Next
        Return added
    End Function

    Private Async Function RefreshAvailableTargetsAsync(Optional preferredTarget As Object = Nothing) As Task
        If IsRefreshingTargets Then Return
        IsRefreshingTargets = True
        RefreshTargetsButton.IsEnabled = False
        Try
            Dim hostApplication As System.Windows.Application = System.Windows.Application.Current
            Dim windows = If(hostApplication Is Nothing, Array.Empty(Of Window)(),
                hostApplication.Windows.Cast(Of Window)().
                    Where(Function(window) Not Object.ReferenceEquals(window, Me) AndAlso window.GetType().Assembly IsNot Me.GetType().Assembly).
                    ToArray())
            Dim discovery = Await Task.Run(
                Function() Tuple.Create(DiscoverStaticObjects(), DiscoverStaticTypes()))
            Dim staticObjects = discovery.Item1
            Dim staticTypes = discovery.Item2

            AllAvailableTargets.Clear()
            For Each window In windows.OrderBy(Function(item) If(item.GetType().FullName, item.GetType().Name))
                Dim watched As Boolean
                SyncLock DebugWatcher.Watchers
                    watched = DebugWatcher.Watchers.Values.Any(
                        Function(candidate) Not candidate.isChild AndAlso Not candidate.IsStaticRoot AndAlso
                                            Object.ReferenceEquals(candidate.AttachedObject, window))
                End SyncLock
                Dim typeName = If(window.GetType().FullName, window.GetType().Name)
                Dim title = If(String.IsNullOrWhiteSpace(window.Title), "untitled", window.Title.Trim())
                AllAvailableTargets.Add(New WatchTargetOption(
                    WatchTargetKind.WindowInstance,
                    "[Window] " & typeName & " — " & title & If(watched, "  (watched)", ""),
                    typeName & " " & title & " window " & If(watched, "watched", ""),
                    window))
            Next

            For Each staticObject In staticObjects
                Dim declaredTypeName = If(staticObject.DeclaredType.FullName, staticObject.DeclaredType.Name)
                AllAvailableTargets.Add(New WatchTargetOption(
                    WatchTargetKind.StaticObject,
                    "[Static object] " & staticObject.SourceName & " : " & declaredTypeName,
                    staticObject.SourceName & " " & declaredTypeName & " static object instance",
                    staticObject))
            Next

            For Each targetType In staticTypes
                Dim watched As Boolean
                SyncLock DebugWatcher.Watchers
                    watched = DebugWatcher.Watchers.Values.Any(
                        Function(candidate) Not candidate.isChild AndAlso candidate.IsStaticRoot AndAlso candidate.TargetType Is targetType)
                End SyncLock
                Dim typeName = If(targetType.FullName, targetType.Name)
                Dim assemblyName = targetType.Assembly.GetName().Name
                AllAvailableTargets.Add(New WatchTargetOption(
                    WatchTargetKind.StaticType,
                    "[Static] " & typeName & "  (" & assemblyName & ")" & If(watched, "  (watched)", ""),
                    typeName & " " & assemblyName & " static " & If(watched, "watched", ""),
                    targetType))
            Next

            LastHostWindowCount = windows.Length
            LastAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length
            ApplyTargetFilter(preferredTarget)
            TargetStatusText.Text = windows.Length & " live window" & If(windows.Length = 1, "", "s") &
                " · " & staticObjects.Count & " static object candidate" & If(staticObjects.Count = 1, "", "s") &
                " · " & staticTypes.Count & " static type" & If(staticTypes.Count = 1, "", "s")
        Finally
            RefreshTargetsButton.IsEnabled = True
            IsRefreshingTargets = False
        End Try
    End Function

    Private Sub ApplyTargetFilter(Optional preferredTarget As Object = Nothing)
        If AvailableTargetsComboBox Is Nothing Then Return
        Dim previous = TryCast(AvailableTargetsComboBox.SelectedItem, WatchTargetOption)
        Dim desiredTarget = If(preferredTarget, If(previous Is Nothing, Nothing, previous.Target))
        Dim filter = If(TargetFilterTextBox Is Nothing, "", TargetFilterTextBox.Text.Trim())

        FilteredAvailableTargets.Clear()
        For Each optionItem In AllAvailableTargets
            If filter.Length = 0 OrElse optionItem.SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 Then
                FilteredAvailableTargets.Add(optionItem)
            End If
        Next

        AvailableTargetsComboBox.SelectedItem = FilteredAvailableTargets.FirstOrDefault(
            Function(optionItem) desiredTarget IsNot Nothing AndAlso Object.ReferenceEquals(optionItem.Target, desiredTarget))
        If AvailableTargetsComboBox.SelectedItem Is Nothing AndAlso FilteredAvailableTargets.Count > 0 Then
            AvailableTargetsComboBox.SelectedIndex = 0
        End If
    End Sub

    Private Shared Function DiscoverStaticTypes() As List(Of Type)
        Dim discovered As New List(Of Type)()
        For Each assembly In AppDomain.CurrentDomain.GetAssemblies()
            If Not IsApplicationAssembly(assembly) Then Continue For

            For Each targetType In GetLoadableTypes(assembly)
                If targetType Is Nothing OrElse targetType.ContainsGenericParameters OrElse targetType.IsEnum OrElse
                   targetType.IsPointer OrElse targetType.IsByRef OrElse targetType.Name.StartsWith("<", StringComparison.Ordinal) Then Continue For
                Dim typeNamespace = If(targetType.Namespace, "")
                If typeNamespace.Equals("System", StringComparison.Ordinal) OrElse
                   typeNamespace.StartsWith("System.", StringComparison.Ordinal) OrElse
                   typeNamespace.Equals("Microsoft", StringComparison.Ordinal) OrElse
                   typeNamespace.StartsWith("Microsoft.", StringComparison.Ordinal) Then Continue For

                Try
                    Dim flags = BindingFlags.Static Or BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.DeclaredOnly
                    Dim hasField = targetType.GetFields(flags).Any(Function(field) Not field.IsLiteral)
                    Dim hasProperty = targetType.GetProperties(flags).Any(
                        Function(prop) prop.GetIndexParameters().Length = 0 AndAlso
                                       prop.GetMethod IsNot Nothing AndAlso prop.GetMethod.IsStatic)
                    If hasField OrElse hasProperty Then discovered.Add(targetType)
                Catch
                End Try
            Next
        Next

        Return discovered.Distinct().
            OrderBy(Function(targetType) If(targetType.FullName, targetType.Name), StringComparer.OrdinalIgnoreCase).
            ToList()
    End Function

    Private Shared Function DiscoverStaticObjects() As List(Of StaticObjectTarget)
        Dim discovered As New List(Of StaticObjectTarget)()
        For Each assembly In AppDomain.CurrentDomain.GetAssemblies()
            If Not IsApplicationAssembly(assembly) Then Continue For

            For Each targetType In GetLoadableTypes(assembly)
                If targetType Is Nothing OrElse targetType.ContainsGenericParameters Then Continue For
                Dim flags = BindingFlags.Static Or BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.DeclaredOnly
                Try
                    For Each field In targetType.GetFields(flags)
                        If field.IsLiteral OrElse field.IsDefined(GetType(System.Runtime.CompilerServices.CompilerGeneratedAttribute), False) OrElse
                           Not CanHoldWatchableObject(field.FieldType) Then Continue For
                        discovered.Add(New StaticObjectTarget(targetType, field, field.FieldType))
                        If discovered.Count >= 500 Then Exit For
                    Next
                    If discovered.Count >= 500 Then Exit For

                    For Each prop In targetType.GetProperties(flags)
                        If prop.GetIndexParameters().Length <> 0 OrElse prop.GetMethod Is Nothing OrElse
                           Not prop.GetMethod.IsStatic OrElse Not CanHoldWatchableObject(prop.PropertyType) Then Continue For
                        discovered.Add(New StaticObjectTarget(targetType, prop, prop.PropertyType))
                        If discovered.Count >= 500 Then Exit For
                    Next
                Catch
                End Try
                If discovered.Count >= 500 Then Exit For
            Next
            If discovered.Count >= 500 Then Exit For
        Next

        Return discovered.OrderBy(Function(item) item.SourceName, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Shared Function CanHoldWatchableObject(targetType As Type) As Boolean
        Return targetType IsNot Nothing AndAlso Not targetType.IsValueType AndAlso targetType IsNot GetType(String) AndAlso
               Not GetType(System.Delegate).IsAssignableFrom(targetType) AndAlso targetType IsNot GetType(Type) AndAlso
               Not targetType.IsPointer AndAlso Not targetType.IsByRef
    End Function

    Private Shared Function IsApplicationAssembly(assembly As Assembly) As Boolean
        If assembly Is Nothing OrElse assembly.IsDynamic OrElse assembly Is GetType(DebugWatcher).Assembly Then Return False
        Dim name = If(assembly.GetName().Name, "")
        If name.Length = 0 OrElse name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) OrElse
           name.Equals("System", StringComparison.OrdinalIgnoreCase) OrElse name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) OrElse
           name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) OrElse name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) OrElse
           name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase) OrElse name.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) OrElse
           name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) OrElse name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) Then Return False

        Try
            Dim location = assembly.Location
            If String.IsNullOrWhiteSpace(location) Then Return assembly Is Assembly.GetEntryAssembly()
            Dim appDirectory = System.IO.Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar) & System.IO.Path.DirectorySeparatorChar
            Return System.IO.Path.GetFullPath(location).StartsWith(appDirectory, StringComparison.OrdinalIgnoreCase)
        Catch
            Return assembly Is Assembly.GetEntryAssembly()
        End Try
    End Function

    Private Shared Function GetLoadableTypes(assembly As Assembly) As IEnumerable(Of Type)
        Try
            Return assembly.GetTypes()
        Catch ex As ReflectionTypeLoadException
            Return ex.Types.Where(Function(targetType) targetType IsNot Nothing).Cast(Of Type)()
        Catch
            Return Array.Empty(Of Type)()
        End Try
    End Function

#End Region

#Region "Caches"

    Private ts As TimeSpan = TimeSpan.FromTicks(1000)
    Private WithEvents CurrentTimeline As ValueTimeline
    Public ConcurrentIterations As New List(Of String)
    Private InitializedWatchers As New Dictionary(Of String, DebugWatcher)
    Private ValueOwnerLookup As New Dictionary(Of String, Tuple(Of DebugValue, DebugWatcher))
    Private _selectedValueGuid As String

#End Region

#Region "Drawing"

    Private _lastRenderSignature As String = ""
    Private Shared ReadOnly TimelinePalette As Color() = {
        Color.FromRgb(69, 199, 120), Color.FromRgb(80, 169, 255),
        Color.FromRgb(242, 174, 73), Color.FromRgb(200, 112, 255),
        Color.FromRgb(255, 104, 125), Color.FromRgb(63, 210, 210)
    }

    Public Sub ClearPoints()
        InvokeUI(Sub()
                     Points.Clear()
                     SpatialCanvas.Children.Clear()
                     SpatialAxisLeft.Children.Clear()
                     SpatialAxisBottom.Children.Clear()
                     ChartCanvas.Children.Clear()
                     StringValueTextBox.Visibility = Visibility.Collapsed
                     StringValueTextBox.Text = ""
                     EmptyGraphMessage.Visibility = Visibility.Visible
                 End Sub)
        _lastRenderSignature = ""
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight
    End Sub

    Public Sub DrawTimeline()
        If CurrentTimeline Is Nothing Then Return
        If Not Dispatcher.CheckAccess() Then
            Dispatcher.Invoke(Sub() DrawTimeline())
            Return
        End If

        Dim snapshots As List(Of DebugValue)
        SyncLock CurrentTimeline.Values
            snapshots = CurrentTimeline.Values.Where(Function(value) value IsNot Nothing).ToList()
        End SyncLock
        If snapshots.Count = 0 Then
            ShowEmptyVisualization("Waiting for the first value snapshot...")
            Return
        End If

        Dim lastIndex As Integer = snapshots.Count - 1
        _Maximum = lastIndex
        Slider.Maximum = lastIndex
        If KeepAtEnd Then
            Slider.UpperValue = lastIndex
            Slider.LowerValue = Math.Max(0, lastIndex - MaxRange)
        End If

        Dim lowerIndex As Integer = Math.Max(0, Math.Min(lastIndex, CInt(Math.Floor(Slider.LowerValue))))
        Dim upperIndex As Integer = Math.Max(lowerIndex, Math.Min(lastIndex, CInt(Math.Ceiling(Slider.UpperValue))))
        Dim visibleValues As List(Of DebugValue) = snapshots.GetRange(lowerIndex, upperIndex - lowerIndex + 1)
        Dim mode As String = SelectedVisualizationMode()
        Dim signature As String = String.Join("|", CurrentTimeline.GUID, snapshots.Count, snapshots.Last().Timecode,
                                              lowerIndex, upperIndex, mode, IsGapCompactionEnabled(), GapCompressionSlider.Value,
                                              Math.Round(ChartCanvas.ActualWidth), Math.Round(ChartCanvas.ActualHeight))
        If signature = _lastRenderSignature Then Return
        _lastRenderSignature = signature

        RenderRichTimeline(visibleValues, mode, lowerIndex, snapshots.Count)
    End Sub

    Private Sub RenderRichTimeline(values As List(Of DebugValue), mode As String, firstIndex As Integer, totalCount As Integer)
        If values.Count = 0 Then
            ShowEmptyVisualization("No snapshots are inside the selected range.")
            Return
        End If

        Dim first As DebugValue = values.First()
        Dim flags As TypeFlags = first.Flags
        Dim title As String = first.Name & "  ·  " & If(first.Type Is Nothing, "Unknown", first.Type.Name)
        Dim elapsedTicks As Long = Math.Max(0L, values.Last().Timecode - values.First().Timecode)
        Dim summary As String = values.Count & " visible / " & totalCount & " snapshots  ·  " & FormatDuration(elapsedTicks)
        If IsGapCompactionEnabled() Then summary &= "  ·  gaps compacted " & GapCompressionSlider.Value.ToString("0") & "%"
        PrepareRichSurface(title, summary)

        Dim isGeometry As Boolean = HasGeometryShape(flags)
        If mode = "Events" Then
            RenderEventTimeline(values, firstIndex)
        ElseIf mode = "Spatial overlay" AndAlso isGeometry Then
            RenderSpatialOverlay(values)
        ElseIf isGeometry Then
            RenderGeometryOverTime(values)
        ElseIf flags.isBoolean Then
            RenderBooleanTimeline(values)
        ElseIf flags.isMediaColor OrElse flags.isDrawingColor Then
            RenderColorTimeline(values)
        ElseIf flags.isEnum OrElse flags.isString Then
            RenderCategoricalTimeline(values)
        ElseIf flags.isThickness Then
            RenderThicknessTimeline(values)
        ElseIf flags.isNumeric OrElse flags.isDateTime OrElse flags.isTimeSpan Then
            RenderNumericTimeline(values)
        Else
            RenderEventTimeline(values, firstIndex)
        End If
    End Sub

    Private Sub PrepareRichSurface(title As String, summary As String)
        Points.Clear()
        ChartCanvas.Children.Clear()
        SpatialCanvas.Children.Clear()
        SpatialAxisLeft.Children.Clear()
        SpatialAxisBottom.Children.Clear()
        p.Visibility = Visibility.Collapsed
        SpatialCanvas.Visibility = Visibility.Collapsed
        SpatialAxisLeft.Visibility = Visibility.Collapsed
        SpatialAxisBottom.Visibility = Visibility.Collapsed
        InvertSpatialCheckBox.Visibility = Visibility.Collapsed
        StringValueTextBox.Visibility = Visibility.Collapsed
        LowLabel.Visibility = Visibility.Collapsed
        HighLabel.Visibility = Visibility.Collapsed
        EmptyGraphMessage.Visibility = Visibility.Collapsed
        ChartCanvas.Visibility = Visibility.Visible
        VisualizationTitle.Text = title
        VisualizationSummary.Text = summary
    End Sub

    Private Sub ShowEmptyVisualization(message As String)
        PrepareRichSurface("Live visualization", "No samples")
        EmptyGraphMessage.Text = message
        EmptyGraphMessage.Visibility = Visibility.Visible
    End Sub

    Private Function SelectedVisualizationMode() As String
        Dim item As ComboBoxItem = TryCast(VisualizationModeComboBox.SelectedItem, ComboBoxItem)
        Return If(item Is Nothing, "Auto", Convert.ToString(item.Content, CultureInfo.InvariantCulture))
    End Function

    Private Sub VisualizationModeComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        _lastRenderSignature = ""
        If IsLoaded AndAlso CurrentTimeline IsNot Nothing Then DrawTimeline()
    End Sub

    Private Sub TimelineSpacing_Changed(sender As Object, e As RoutedEventArgs)
        _lastRenderSignature = ""
        If IsLoaded AndAlso CurrentTimeline IsNot Nothing Then DrawTimeline()
    End Sub

    Private Function IsGapCompactionEnabled() As Boolean
        Return CompactGapsCheckBox IsNot Nothing AndAlso CompactGapsCheckBox.IsChecked = True
    End Function

    Private Function ChartBounds() As Rect
        Dim width As Double = Math.Max(120, ChartCanvas.ActualWidth)
        Dim height As Double = Math.Max(100, ChartCanvas.ActualHeight)
        Return New Rect(58, 18, Math.Max(40, width - 78), Math.Max(40, height - 58))
    End Function

    Private Sub DrawChartGrid(bounds As Rect, Optional horizontalLines As Integer = 4, Optional verticalLines As Integer = 6)
        Dim gridBrush As New SolidColorBrush(Color.FromRgb(54, 63, 69))
        For i As Integer = 0 To horizontalLines
            Dim y As Double = bounds.Top + bounds.Height * i / Math.Max(1, horizontalLines)
            AddLine(ChartCanvas, bounds.Left, y, bounds.Right, y, gridBrush, 1)
        Next
        For i As Integer = 0 To verticalLines
            Dim x As Double = bounds.Left + bounds.Width * i / Math.Max(1, verticalLines)
            AddLine(ChartCanvas, x, bounds.Top, x, bounds.Bottom, gridBrush, 1)
        Next
    End Sub

    Private Sub AddLine(canvas As Canvas, x1 As Double, y1 As Double, x2 As Double, y2 As Double, brush As Brush, thickness As Double)
        canvas.Children.Add(New Line With {.X1 = x1, .Y1 = y1, .X2 = x2, .Y2 = y2, .Stroke = brush, .StrokeThickness = thickness})
    End Sub

    Private Sub AddCanvasText(canvas As Canvas, text As String, x As Double, y As Double,
                              Optional foreground As Brush = Nothing, Optional fontSize As Double = 10,
                              Optional maxWidth As Double = Double.NaN)
        Dim label As New TextBlock With {
            .Text = text,
            .Foreground = If(foreground, New SolidColorBrush(Color.FromRgb(174, 184, 178))),
            .FontSize = fontSize,
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        If Not Double.IsNaN(maxWidth) Then label.MaxWidth = maxWidth
        Canvas.SetLeft(label, x)
        Canvas.SetTop(label, y)
        canvas.Children.Add(label)
    End Sub

    Private Function TimePosition(value As DebugValue, values As List(Of DebugValue), bounds As Rect) As Double
        If values.Count <= 1 Then Return bounds.Left + bounds.Width / 2
        Dim startTicks As Long = values.First().Timecode
        Dim duration As Long = values.Last().Timecode - startTicks
        Dim index As Integer = values.IndexOf(value)
        Dim indexFraction As Double = index / CDbl(Math.Max(1, values.Count - 1))
        Dim timeFraction As Double = If(duration <= 0, indexFraction, (value.Timecode - startTicks) / CDbl(duration))
        Dim fraction As Double = timeFraction
        If IsGapCompactionEnabled() Then
            Dim compression As Double = Math.Max(0, Math.Min(1, GapCompressionSlider.Value / 100.0))
            fraction = timeFraction * (1 - compression) + indexFraction * compression
        End If
        Return bounds.Left + bounds.Width * fraction
    End Function

    Private Sub DrawTimeLabels(values As List(Of DebugValue), bounds As Rect)
        Dim startTicks As Long = values.First().Timecode
        Dim duration As Long = Math.Max(0, values.Last().Timecode - startTicks)
        If IsGapCompactionEnabled() AndAlso values.Count > 1 Then
            For i As Integer = 0 To 4
                Dim index As Integer = CInt(Math.Round((values.Count - 1) * i / 4.0))
                Dim sample As DebugValue = values(index)
                AddCanvasText(ChartCanvas, FormatRelativeTime(sample.Timecode - startTicks), TimePosition(sample, values, bounds) - 18, bounds.Bottom + 8)
            Next
        Else
            For i As Integer = 0 To 4
                Dim fraction As Double = i / 4.0
                AddCanvasText(ChartCanvas, FormatRelativeTime(CLng(duration * fraction)), bounds.Left + bounds.Width * fraction - 18, bounds.Bottom + 8)
            Next
        End If
    End Sub

    Private Sub RenderNumericTimeline(values As List(Of DebugValue))
        Dim points As New List(Of Tuple(Of DebugValue, Double))
        For Each value In values
            Dim number As Double
            If TryGetNumericValue(value.Value, number) AndAlso Not Double.IsNaN(number) AndAlso Not Double.IsInfinity(number) Then
                points.Add(Tuple.Create(value, number))
            End If
        Next
        If points.Count = 0 Then
            ShowEmptyVisualization("This value has no numeric samples to draw.")
            Return
        End If

        Dim bounds As Rect = ChartBounds()
        DrawChartGrid(bounds)
        Dim minimum As Double = points.Min(Function(point) point.Item2)
        Dim maximum As Double = points.Max(Function(point) point.Item2)
        If minimum = maximum Then
            Dim padding As Double = Math.Max(1, Math.Abs(minimum) * 0.1)
            minimum -= padding
            maximum += padding
        Else
            Dim padding As Double = (maximum - minimum) * 0.08
            minimum -= padding
            maximum += padding
        End If

        For i As Integer = 0 To 4
            Dim fraction As Double = i / 4.0
            Dim number As Double = maximum - (maximum - minimum) * fraction
            AddCanvasText(ChartCanvas, number.ToString("0.###"), 5, bounds.Top + bounds.Height * fraction - 7, Nothing, 9, 50)
        Next
        DrawTimeLabels(values, bounds)

        Dim samplePoints As New PointCollection()
        For Each point In points
            Dim x As Double = TimePosition(point.Item1, values, bounds)
            Dim y As Double = bounds.Bottom - (point.Item2 - minimum) / (maximum - minimum) * bounds.Height
            samplePoints.Add(New Point(x, y))
        Next

        ' A value remains in effect until the next observed change. Drawing a step
        ' line makes long quiet periods visible instead of implying interpolation.
        Dim linePoints As New PointCollection From {samplePoints.First()}
        For i As Integer = 1 To samplePoints.Count - 1
            linePoints.Add(New Point(samplePoints(i).X, samplePoints(i - 1).Y))
            linePoints.Add(samplePoints(i))
        Next

        Dim areaPoints As New PointCollection From {New Point(linePoints.First().X, bounds.Bottom)}
        For Each point In linePoints
            areaPoints.Add(point)
        Next
        areaPoints.Add(New Point(linePoints.Last().X, bounds.Bottom))
        ChartCanvas.Children.Add(New Polygon With {
            .Points = areaPoints,
            .Fill = New SolidColorBrush(Color.FromArgb(45, 69, 199, 120)),
            .StrokeThickness = 0
        })
        ChartCanvas.Children.Add(New Polyline With {
            .Points = linePoints,
            .Stroke = New SolidColorBrush(TimelinePalette(0)),
            .StrokeThickness = 2.5,
            .StrokeLineJoin = PenLineJoin.Round
        })

        Dim markerStride As Integer = Math.Max(1, CInt(Math.Ceiling(points.Count / 120.0)))
        For i As Integer = 0 To points.Count - 1 Step markerStride
            Dim marker As New Ellipse With {.Width = 7, .Height = 7, .Fill = New SolidColorBrush(TimelinePalette(0))}
            marker.ToolTip = points(i).Item2.ToString("0.###") & " at " & FormatElapsed(points(i).Item1, values)
            Canvas.SetLeft(marker, samplePoints(i).X - 3.5)
            Canvas.SetTop(marker, samplePoints(i).Y - 3.5)
            ChartCanvas.Children.Add(marker)
        Next
        VisualizationSummary.Text &= "  ·  range " & minimum.ToString("0.###") & " – " & maximum.ToString("0.###")
    End Sub

    Private Sub RenderBooleanTimeline(values As List(Of DebugValue))
        Dim bounds As Rect = ChartBounds()
        DrawChartGrid(bounds, 2, 6)
        Dim bandTop As Double = bounds.Top + bounds.Height * 0.18
        Dim bandHeight As Double = bounds.Height * 0.64
        For i As Integer = 0 To values.Count - 1
            Dim x1 As Double = If(values.Count = 1, bounds.Left, TimePosition(values(i), values, bounds))
            Dim x2 As Double = If(i = values.Count - 1, bounds.Right, TimePosition(values(i + 1), values, bounds))
            If values.Count = 1 Then x2 = bounds.Right
            Dim state As Boolean = Convert.ToBoolean(values(i).Value, CultureInfo.InvariantCulture)
            Dim color As Color = If(state, Color.FromRgb(54, 201, 113), Color.FromRgb(224, 82, 82))
            Dim block As New System.Windows.Shapes.Rectangle With {
                .Width = Math.Max(2, x2 - x1), .Height = bandHeight,
                .Fill = New SolidColorBrush(Color.FromArgb(185, color.R, color.G, color.B)),
                .Stroke = New SolidColorBrush(color), .StrokeThickness = 1
            }
            block.ToolTip = state.ToString() & " from " & FormatElapsed(values(i), values)
            Canvas.SetLeft(block, x1)
            Canvas.SetTop(block, bandTop)
            ChartCanvas.Children.Add(block)
            If x2 - x1 > 46 Then AddCanvasText(ChartCanvas, If(state, "TRUE", "FALSE"), x1 + 6, bandTop + bandHeight / 2 - 8, Brushes.White, 11)
        Next
        AddCanvasText(ChartCanvas, "TRUE", 10, bandTop - 2, New SolidColorBrush(Color.FromRgb(54, 201, 113)), 9)
        AddCanvasText(ChartCanvas, "FALSE", 8, bandTop + bandHeight - 12, New SolidColorBrush(Color.FromRgb(224, 82, 82)), 9)
        DrawTimeLabels(values, bounds)
    End Sub

    Private Sub RenderCategoricalTimeline(values As List(Of DebugValue))
        Dim bounds As Rect = ChartBounds()
        DrawChartGrid(bounds, 2, 6)
        Dim categories As New List(Of String)
        For Each value In values
            Dim category As String = SafeDisplay(value.Value)
            If Not categories.Contains(category) Then categories.Add(category)
        Next
        For i As Integer = 0 To values.Count - 1
            Dim category As String = SafeDisplay(values(i).Value)
            Dim categoryIndex As Integer = categories.IndexOf(category)
            Dim x1 As Double = If(values.Count = 1, bounds.Left, TimePosition(values(i), values, bounds))
            Dim x2 As Double = If(i = values.Count - 1, bounds.Right, TimePosition(values(i + 1), values, bounds))
            If values.Count = 1 Then x2 = bounds.Right
            Dim color As Color = TimelinePalette(categoryIndex Mod TimelinePalette.Length)
            Dim block As New System.Windows.Shapes.Rectangle With {
                .Width = Math.Max(3, x2 - x1), .Height = bounds.Height * 0.62,
                .Fill = New SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)),
                .Stroke = New SolidColorBrush(color), .StrokeThickness = 1
            }
            block.ToolTip = category & " at " & FormatElapsed(values(i), values)
            Canvas.SetLeft(block, x1)
            Canvas.SetTop(block, bounds.Top + bounds.Height * 0.18)
            ChartCanvas.Children.Add(block)
            If x2 - x1 > 55 Then AddCanvasText(ChartCanvas, category, x1 + 5, bounds.Top + bounds.Height * 0.44, Brushes.White, 10, x2 - x1 - 8)
        Next
        Dim legendX As Double = bounds.Left
        For i As Integer = 0 To Math.Min(categories.Count, TimelinePalette.Length) - 1
            AddCanvasText(ChartCanvas, "? " & categories(i), legendX, 0, New SolidColorBrush(TimelinePalette(i)), 9, 120)
            legendX += 125
        Next
        DrawTimeLabels(values, bounds)
    End Sub

    Private Sub RenderColorTimeline(values As List(Of DebugValue))
        Dim bounds As Rect = ChartBounds()
        DrawChartGrid(bounds, 2, 6)
        For i As Integer = 0 To values.Count - 1
            Dim x1 As Double = If(values.Count = 1, bounds.Left, TimePosition(values(i), values, bounds))
            Dim x2 As Double = If(i = values.Count - 1, bounds.Right, TimePosition(values(i + 1), values, bounds))
            If values.Count = 1 Then x2 = bounds.Right
            Dim color As Color = ToMediaColor(values(i).Value)
            Dim block As New System.Windows.Shapes.Rectangle With {
                .Width = Math.Max(3, x2 - x1), .Height = bounds.Height * 0.7,
                .Fill = New SolidColorBrush(color), .Stroke = Brushes.White, .StrokeThickness = 0.5
            }
            block.ToolTip = color.ToString() & " at " & FormatElapsed(values(i), values)
            Canvas.SetLeft(block, x1)
            Canvas.SetTop(block, bounds.Top + bounds.Height * 0.12)
            ChartCanvas.Children.Add(block)
        Next
        DrawTimeLabels(values, bounds)
    End Sub

    Private Sub RenderThicknessTimeline(values As List(Of DebugValue))
        Dim bounds As Rect = ChartBounds()
        DrawChartGrid(bounds)
        Dim allNumbers As New List(Of Double)
        For Each value In values
            Dim thickness As Thickness = DirectCast(value.Value, Thickness)
            allNumbers.AddRange({thickness.Left, thickness.Top, thickness.Right, thickness.Bottom})
        Next
        Dim minimum As Double = allNumbers.Min()
        Dim maximum As Double = allNumbers.Max()
        If minimum = maximum Then maximum = minimum + 1
        Dim labels As String() = {"Left", "Top", "Right", "Bottom"}
        For component As Integer = 0 To 3
            Dim linePoints As New PointCollection()
            For Each value In values
                Dim thickness As Thickness = DirectCast(value.Value, Thickness)
                Dim number As Double = {thickness.Left, thickness.Top, thickness.Right, thickness.Bottom}(component)
                linePoints.Add(New Point(TimePosition(value, values, bounds), bounds.Bottom - (number - minimum) / (maximum - minimum) * bounds.Height))
            Next
            ChartCanvas.Children.Add(New Polyline With {.Points = linePoints, .Stroke = New SolidColorBrush(TimelinePalette(component)), .StrokeThickness = 2})
            AddCanvasText(ChartCanvas, "? " & labels(component), bounds.Left + component * 80, 0, New SolidColorBrush(TimelinePalette(component)), 9)
        Next
        AddCanvasText(ChartCanvas, maximum.ToString("0.##"), 5, bounds.Top - 7, Nothing, 9)
        AddCanvasText(ChartCanvas, minimum.ToString("0.##"), 5, bounds.Bottom - 7, Nothing, 9)
        DrawTimeLabels(values, bounds)
    End Sub

    Private Sub RenderGeometryOverTime(values As List(Of DebugValue))
        Dim geometry As New List(Of Tuple(Of DebugValue, Double, Double, Double, Double, Boolean))
        For Each value In values
            Dim x As Double, y As Double, width As Double, height As Double, isRectangle As Boolean
            If TryGetGeometry(value, x, y, width, height, isRectangle) Then
                geometry.Add(Tuple.Create(value, x, y, width, height, isRectangle))
            End If
        Next
        If geometry.Count = 0 Then
            ShowEmptyVisualization("No geometry samples are available in this range.")
            Return
        End If

        Dim bounds As Rect = ChartBounds()
        DrawChartGrid(bounds)
        Dim minY As Double = geometry.Min(Function(item) item.Item3)
        Dim maxY As Double = geometry.Max(Function(item) item.Item3 + item.Item5)
        If minY = maxY Then maxY = minY + 1
        Dim maxWidth As Double = Math.Max(1, geometry.Max(Function(item) Math.Abs(item.Item4)))
        Dim maxHeight As Double = Math.Max(1, geometry.Max(Function(item) Math.Abs(item.Item5)))
        Dim centerPath As New PointCollection()
        Dim stride As Integer = Math.Max(1, CInt(Math.Ceiling(geometry.Count / 80.0)))
        For i As Integer = 0 To geometry.Count - 1 Step stride
            Dim item = geometry(i)
            Dim xTime As Double = TimePosition(item.Item1, values, bounds)
            Dim yScreen As Double = bounds.Bottom - (item.Item3 - minY) / (maxY - minY) * bounds.Height
            centerPath.Add(New Point(xTime, yScreen))
            Dim t As Double = If(geometry.Count = 1, 1, i / CDbl(geometry.Count - 1))
            Dim color As Color = TimeGradient(t)
            If item.Item6 Then
                Dim width As Double = Math.Max(5, Math.Abs(item.Item4) / maxWidth * 56)
                Dim height As Double = Math.Max(5, Math.Abs(item.Item5) / maxHeight * Math.Min(90, bounds.Height * 0.45))
                Dim rectangle As New System.Windows.Shapes.Rectangle With {
                    .Width = width, .Height = height,
                    .Stroke = New SolidColorBrush(color), .StrokeThickness = If(t > 0.95, 3, 1.5),
                    .Fill = New SolidColorBrush(Color.FromArgb(CByte(35 + t * 80), color.R, color.G, color.B))
                }
                rectangle.ToolTip = GeometryTooltip(item.Item2, item.Item3, item.Item4, item.Item5, item.Item1, values)
                Canvas.SetLeft(rectangle, xTime - width / 2)
                Canvas.SetTop(rectangle, yScreen - height / 2)
                ChartCanvas.Children.Add(rectangle)
            Else
                Dim marker As New Ellipse With {.Width = 8, .Height = 8, .Fill = New SolidColorBrush(color)}
                marker.ToolTip = GeometryTooltip(item.Item2, item.Item3, item.Item4, item.Item5, item.Item1, values)
                Canvas.SetLeft(marker, xTime - 4)
                Canvas.SetTop(marker, yScreen - 4)
                ChartCanvas.Children.Add(marker)
            End If
        Next
        If centerPath.Count > 1 Then
            ChartCanvas.Children.Insert(0,
                                        New Polyline With {.Points = centerPath, .Stroke = New SolidColorBrush(Color.FromArgb(150, 120, 205, 255)), .StrokeThickness = 1.5})
        End If
        AddCanvasText(ChartCanvas, "Geometry position / shape over time", bounds.Left, 0, Brushes.White, 10)
        AddCanvasText(ChartCanvas, maxY.ToString("0.##"), 5, bounds.Top - 7, Nothing, 9)
        AddCanvasText(ChartCanvas, minY.ToString("0.##"), 5, bounds.Bottom - 7, Nothing, 9)
        DrawTimeLabels(values, bounds)
    End Sub

    Private Sub RenderSpatialOverlay(values As List(Of DebugValue))
        ChartCanvas.Visibility = Visibility.Collapsed
        SpatialCanvas.Visibility = Visibility.Visible
        EmptyGraphMessage.Visibility = Visibility.Collapsed
        SpatialCanvas.Children.Clear()
        Dim width As Double = Math.Max(120, SpatialCanvas.ActualWidth)
        Dim height As Double = Math.Max(100, SpatialCanvas.ActualHeight)
        Dim bounds As New Rect(48, 18, Math.Max(40, width - 68), Math.Max(40, height - 48))
        Dim geometry As New List(Of Tuple(Of DebugValue, Double, Double, Double, Double, Boolean))
        For Each value In values
            Dim x As Double, y As Double, itemWidth As Double, itemHeight As Double, isRectangle As Boolean
            If TryGetGeometry(value, x, y, itemWidth, itemHeight, isRectangle) Then geometry.Add(Tuple.Create(value, x, y, itemWidth, itemHeight, isRectangle))
        Next
        If geometry.Count = 0 Then Return
        Dim minX As Double = geometry.Min(Function(item) item.Item2)
        Dim maxX As Double = geometry.Max(Function(item) item.Item2 + item.Item4)
        Dim minY As Double = geometry.Min(Function(item) item.Item3)
        Dim maxY As Double = geometry.Max(Function(item) item.Item3 + item.Item5)
        If minX = maxX Then maxX = minX + 1
        If minY = maxY Then maxY = minY + 1
        Dim padX As Double = (maxX - minX) * 0.08
        Dim padY As Double = (maxY - minY) * 0.08
        minX -= padX : maxX += padX : minY -= padY : maxY += padY
        Dim gridBrush As New SolidColorBrush(Color.FromRgb(54, 63, 69))
        For i As Integer = 0 To 5
            Dim gx As Double = bounds.Left + bounds.Width * i / 5
            Dim gy As Double = bounds.Top + bounds.Height * i / 5
            AddLine(SpatialCanvas, gx, bounds.Top, gx, bounds.Bottom, gridBrush, 1)
            AddLine(SpatialCanvas, bounds.Left, gy, bounds.Right, gy, gridBrush, 1)
        Next
        Dim centers As New PointCollection()
        For i As Integer = 0 To geometry.Count - 1
            Dim item = geometry(i)
            Dim x1 As Double = bounds.Left + (item.Item2 - minX) / (maxX - minX) * bounds.Width
            Dim y1 As Double = bounds.Top + (item.Item3 - minY) / (maxY - minY) * bounds.Height
            Dim x2 As Double = bounds.Left + (item.Item2 + item.Item4 - minX) / (maxX - minX) * bounds.Width
            Dim y2 As Double = bounds.Top + (item.Item3 + item.Item5 - minY) / (maxY - minY) * bounds.Height
            centers.Add(New Point((x1 + x2) / 2, (y1 + y2) / 2))
            Dim t As Double = If(geometry.Count = 1, 1, i / CDbl(geometry.Count - 1))
            Dim color As Color = TimeGradient(t)
            If item.Item6 Then
                Dim rectangle As New System.Windows.Shapes.Rectangle With {
                    .Width = Math.Max(2, Math.Abs(x2 - x1)), .Height = Math.Max(2, Math.Abs(y2 - y1)),
                    .Stroke = New SolidColorBrush(color), .StrokeThickness = If(i = geometry.Count - 1, 3, 1.5),
                    .Fill = New SolidColorBrush(Color.FromArgb(CByte(25 + t * 75), color.R, color.G, color.B))
                }
                rectangle.ToolTip = GeometryTooltip(item.Item2, item.Item3, item.Item4, item.Item5, item.Item1, values)
                Canvas.SetLeft(rectangle, Math.Min(x1, x2)) : Canvas.SetTop(rectangle, Math.Min(y1, y2))
                SpatialCanvas.Children.Add(rectangle)
            Else
                Dim marker As New Ellipse With {.Width = 9, .Height = 9, .Fill = New SolidColorBrush(color)}
                marker.ToolTip = GeometryTooltip(item.Item2, item.Item3, item.Item4, item.Item5, item.Item1, values)
                Canvas.SetLeft(marker, x1 - 4.5) : Canvas.SetTop(marker, y1 - 4.5)
                SpatialCanvas.Children.Add(marker)
            End If
        Next
        If centers.Count > 1 Then SpatialCanvas.Children.Insert(0, New Polyline With {.Points = centers, .Stroke = New SolidColorBrush(Color.FromArgb(170, 120, 205, 255)), .StrokeThickness = 2})
        AddCanvasText(SpatialCanvas, "Old", bounds.Left, 0, New SolidColorBrush(TimeGradient(0)), 10)
        AddCanvasText(SpatialCanvas, "Newest", bounds.Right - 50, 0, New SolidColorBrush(TimeGradient(1)), 10)
        VisualizationSummary.Text &= "  ·  X " & minX.ToString("0.##") & "–" & maxX.ToString("0.##") & "  Y " & minY.ToString("0.##") & "–" & maxY.ToString("0.##")
    End Sub

    Private Sub RenderEventTimeline(values As List(Of DebugValue), firstIndex As Integer)
        Dim bounds As Rect = ChartBounds()
        DrawChartGrid(bounds, 1, 6)
        Dim displayValues As List(Of DebugValue) = values
        If displayValues.Count > 80 Then displayValues = displayValues.Where(Function(value, index) index Mod CInt(Math.Ceiling(values.Count / 80.0)) = 0 OrElse index = values.Count - 1).ToList()
        For i As Integer = 0 To displayValues.Count - 1
            Dim value As DebugValue = displayValues(i)
            Dim x As Double = TimePosition(value, values, bounds)
            Dim color As Color = TimelinePalette(i Mod TimelinePalette.Length)
            AddLine(ChartCanvas, x, bounds.Top + 20, x, bounds.Bottom, New SolidColorBrush(color), 2)
            Dim marker As New Ellipse With {.Width = 10, .Height = 10, .Fill = New SolidColorBrush(color)}
            marker.ToolTip = SafeDisplay(value.Value) & " at " & FormatElapsed(value, values)
            Canvas.SetLeft(marker, x - 5) : Canvas.SetTop(marker, bounds.Top + 15)
            ChartCanvas.Children.Add(marker)
            If i Mod Math.Max(1, CInt(Math.Ceiling(displayValues.Count / 8.0))) = 0 Then
                AddCanvasText(ChartCanvas, SafeDisplay(value.Value), x + 4, bounds.Top + 34 + (i Mod 3) * 22, Brushes.White, 9, 130)
            End If
        Next
        AddCanvasText(ChartCanvas, "Value changes", bounds.Left, 0, Brushes.White, 10)
        DrawTimeLabels(values, bounds)
    End Sub

    Private Function TryGetNumericValue(value As Object, ByRef number As Double) As Boolean
        If value Is Nothing Then Return False
        If TypeOf value Is DateTime Then
            number = DirectCast(value, DateTime).ToOADate()
            Return True
        End If
        If TypeOf value Is TimeSpan Then
            number = DirectCast(value, TimeSpan).TotalMilliseconds
            Return True
        End If
        If value.GetType().IsEnum Then
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture)
            Return True
        End If
        Return Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, number)
    End Function

    Private Function HasGeometryShape(flags As TypeFlags) As Boolean
        Return flags.isDrawingPoint OrElse flags.isWindowsPoint OrElse flags.isDrawingRectangle OrElse
               flags.isShapesRect OrElse flags.isWindowsRect OrElse flags.isVector OrElse
               flags.isDrawingSize OrElse flags.isWindowsSize
    End Function

    Private Function TryGetGeometry(value As DebugValue, ByRef x As Double, ByRef y As Double,
                                    ByRef width As Double, ByRef height As Double, ByRef isRectangle As Boolean) As Boolean
        If value Is Nothing OrElse value.Value Is Nothing Then Return False
        Dim flags As TypeFlags = value.Flags
        If flags.isDrawingPoint Then
            Dim point As System.Drawing.Point = DirectCast(value.Value, System.Drawing.Point)
            x = point.X : y = point.Y
        ElseIf flags.isWindowsPoint Then
            Dim point As System.Windows.Point = DirectCast(value.Value, System.Windows.Point)
            x = point.X : y = point.Y
        ElseIf flags.isVector Then
            Dim vector As System.Windows.Vector = DirectCast(value.Value, System.Windows.Vector)
            x = vector.X : y = vector.Y
        ElseIf flags.isDrawingRectangle Then
            Dim rectangle As System.Drawing.Rectangle = DirectCast(value.Value, System.Drawing.Rectangle)
            x = rectangle.X : y = rectangle.Y : width = rectangle.Width : height = rectangle.Height : isRectangle = True
        ElseIf flags.isWindowsRect Then
            Dim rectangle As System.Windows.Rect = DirectCast(value.Value, System.Windows.Rect)
            x = rectangle.X : y = rectangle.Y : width = rectangle.Width : height = rectangle.Height : isRectangle = True
        ElseIf flags.isShapesRect Then
            Dim rectangle As System.Windows.Shapes.Rectangle = DirectCast(value.Value, System.Windows.Shapes.Rectangle)
            x = rectangle.Margin.Left : y = rectangle.Margin.Top
            width = If(Double.IsNaN(rectangle.Width), rectangle.ActualWidth, rectangle.Width)
            height = If(Double.IsNaN(rectangle.Height), rectangle.ActualHeight, rectangle.Height)
            isRectangle = True
        ElseIf flags.isDrawingSize Then
            Dim size As System.Drawing.Size = DirectCast(value.Value, System.Drawing.Size)
            width = size.Width : height = size.Height : isRectangle = True
        ElseIf flags.isWindowsSize Then
            Dim size As System.Windows.Size = DirectCast(value.Value, System.Windows.Size)
            width = size.Width : height = size.Height : isRectangle = True
        Else
            Return False
        End If
        Return True
    End Function

    Private Function TimeGradient(amount As Double) As Color
        amount = Math.Max(0, Math.Min(1, amount))
        Return Color.FromRgb(CByte(70 + 185 * amount), CByte(195 - 105 * amount), CByte(255 - 35 * amount))
    End Function

    Private Function ToMediaColor(value As Object) As Color
        If TypeOf value Is Color Then Return DirectCast(value, Color)
        If TypeOf value Is System.Drawing.Color Then
            Dim drawingColor As System.Drawing.Color = DirectCast(value, System.Drawing.Color)
            Return Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B)
        End If
        Return Colors.Transparent
    End Function

    Private Function SafeDisplay(value As Object) As String
        If value Is Nothing Then Return "NULL"
        Try
            Dim text As String = Convert.ToString(value, CultureInfo.InvariantCulture)
            If text Is Nothing Then Return value.GetType().Name
            text = text.Replace(vbCr, " ").Replace(vbLf, " ")
            Return If(text.Length > 80, text.Substring(0, 77) & "...", text)
        Catch
            Return value.GetType().Name
        End Try
    End Function

    Private Function FormatElapsed(value As DebugValue, values As List(Of DebugValue)) As String
        Return FormatRelativeTime(Math.Max(0L, value.Timecode - values.First().Timecode))
    End Function

    Private Function FormatRelativeTime(elapsedTicks As Long) As String
        Return "+" & FormatDuration(Math.Max(0L, elapsedTicks))
    End Function

    Private Function FormatDuration(elapsedTicks As Long) As String
        Dim elapsed As TimeSpan = TimeSpan.FromTicks(Math.Max(0L, elapsedTicks))
        If elapsedTicks >= TimeSpan.TicksPerSecond Then Return elapsed.TotalSeconds.ToString("0.###") & " s"
        If elapsedTicks >= TimeSpan.TicksPerMillisecond Then Return elapsed.TotalMilliseconds.ToString("0.###") & " ms"
        If elapsedTicks > 0 Then Return (elapsedTicks / 10.0).ToString("0.###") & " µs"
        Return "0 ms"
    End Function

    Private Function GeometryTooltip(x As Double, y As Double, width As Double, height As Double,
                                     value As DebugValue, values As List(Of DebugValue)) As String
        Return "X=" & x.ToString("0.###") & ", Y=" & y.ToString("0.###") &
               ", W=" & width.ToString("0.###") & ", H=" & height.ToString("0.###") &
               "  " & FormatElapsed(value, values)
    End Function

    Private Sub DrawLegacyTimeline()
        If CurrentTimeline Is Nothing Then Return

        Maximum = CurrentTimeline.Maximum - 1

        If KeepAtEnd Then
            Upper = Maximum
        End If

        If isAtEnd Then
            If IsMaxWidth AndAlso Not isUserChangingLower Then
                Range = MaxRange
                Lower = Upper - Range
            ElseIf Not isUserChangingLower Then
                Lower = Upper - Range
            End If
        End If

        If isAtBeginning AndAlso Not isUserChangingLower Then
            If IsMaxWidth Then
                Range = MaxRange
                Lower = Upper - Range
            Else
                Lower = 0
            End If
            'ElseIf IsMaxWidth AndAlso isUserChangingLower Then
            '    Upper = Math.Min(Lower + Range, MaxRange)
        End If

        If DrawIndex = Upper Then Return

        Dim splice As ValueTimelineSplice = CurrentTimeline.GetValuesWithin(Lower, Upper)
        If splice.isGraphable Then
            Dim Flags As TypeFlags = splice.Values.First().Flags
            Dim isSpatialType As Boolean = Flags.isDrawingPoint OrElse Flags.isWindowsPoint OrElse Flags.isDrawingRectangle OrElse Flags.isShapesRect OrElse Flags.isWindowsRect OrElse Flags.isVector OrElse Flags.isDrawingSize OrElse Flags.isWindowsSize
            If Not isSpatialType Then
                If CurrentTimeline.HighestValue Is Nothing Then Return
                If CurrentTimeline.LowestValue Is Nothing Then Return
            End If
            If isSpatialType Then
                InvokeUI(
                    Sub()
                        p.Visibility = Visibility.Collapsed
                        StringValueTextBox.Visibility = Visibility.Collapsed
                        SpatialCanvas.Visibility = Visibility.Visible
                        SpatialAxisLeft.Visibility = Visibility.Visible
                        SpatialAxisBottom.Visibility = Visibility.Visible
                        InvertSpatialCheckBox.Visibility = Visibility.Visible
                        Dim invertY As Boolean = InvertSpatialCheckBox.IsChecked = True
                        If SpatialCanvas.Children.Count > 0 Then SpatialCanvas.Children.Clear()
                        If SpatialAxisLeft.Children.Count > 0 Then SpatialAxisLeft.Children.Clear()
                        If SpatialAxisBottom.Children.Count > 0 Then SpatialAxisBottom.Children.Clear()
                        Dim circleSize As Double = 10
                        ' Compute viewport bounds from visible splice values to keep last point in view
                        Dim leftVal As Double = Double.MaxValue
                        Dim rightVal As Double = Double.MinValue
                        Dim topVal As Double = Double.MaxValue
                        Dim bottomVal As Double = Double.MinValue
                        For Each sv As DebugValue In splice.Values
                            Dim svFlags As TypeFlags = sv.Flags
                            Dim sx As Double = 0
                            Dim sy As Double = 0
                            Dim sw As Double = 0
                            Dim sh As Double = 0
                            If svFlags.isDrawingPoint Then
                                Dim sPt As System.Drawing.Point = DirectCast(sv.Value, System.Drawing.Point)
                                sx = sPt.X : sy = sPt.Y
                            ElseIf svFlags.isWindowsPoint Then
                                Dim sPt As System.Windows.Point = DirectCast(sv.Value, System.Windows.Point)
                                sx = sPt.X : sy = sPt.Y
                            ElseIf svFlags.isVector Then
                                Dim sVec As System.Windows.Vector = DirectCast(sv.Value, System.Windows.Vector)
                                sx = sVec.X : sy = sVec.Y
                            ElseIf svFlags.isDrawingRectangle Then
                                Dim sRect As System.Drawing.Rectangle = DirectCast(sv.Value, System.Drawing.Rectangle)
                                sx = sRect.X : sy = sRect.Y : sw = sRect.Width : sh = sRect.Height
                            ElseIf svFlags.isShapesRect Then
                                Dim sRect As System.Windows.Shapes.Rectangle = DirectCast(sv.Value, System.Windows.Shapes.Rectangle)
                                sx = sRect.Margin.Left : sy = sRect.Margin.Top
                                sw = If(Double.IsNaN(sRect.Width), 0, sRect.Width)
                                sh = If(Double.IsNaN(sRect.Height), 0, sRect.Height)
                            ElseIf svFlags.isWindowsRect Then
                                Dim sRect As System.Windows.Rect = DirectCast(sv.Value, System.Windows.Rect)
                                sx = sRect.X : sy = sRect.Y : sw = sRect.Width : sh = sRect.Height
                            ElseIf svFlags.isDrawingSize Then
                                Dim sSize As System.Drawing.Size = DirectCast(sv.Value, System.Drawing.Size)
                                sx = 0 : sy = 0 : sw = sSize.Width : sh = sSize.Height
                            ElseIf svFlags.isWindowsSize Then
                                Dim sSize As System.Windows.Size = DirectCast(sv.Value, System.Windows.Size)
                                sx = 0 : sy = 0 : sw = sSize.Width : sh = sSize.Height
                            End If
                            If sx < leftVal Then leftVal = sx
                            If sx + sw > rightVal Then rightVal = sx + sw
                            If sx > rightVal Then rightVal = sx
                            If sy < topVal Then topVal = sy
                            If sy + sh > bottomVal Then bottomVal = sy + sh
                            If sy > bottomVal Then bottomVal = sy
                        Next
                        If leftVal = Double.MaxValue Then leftVal = 0
                        If rightVal = Double.MinValue Then rightVal = 0
                        If topVal = Double.MaxValue Then topVal = 0
                        If bottomVal = Double.MinValue Then bottomVal = 0
                        If rightVal = leftVal Then rightVal = leftVal + 1
                        If bottomVal = topVal Then bottomVal = topVal + 1
                        Dim padX As Double = (rightVal - leftVal) * 0.1
                        Dim padY As Double = (bottomVal - topVal) * 0.1
                        leftVal -= padX
                        rightVal += padX
                        topVal -= padY
                        bottomVal += padY

                        ' Draw static background grid with dark red lines ~20px apart
                        Dim gridSpacing As Double = 20
                        Dim gridBrush As New SolidColorBrush(Color.FromArgb(255, 80, 10, 10))
                        Dim x As Double = 0
                        While x <= PlotWidth
                            Dim gridLine As New Line()
                            gridLine.X1 = x : gridLine.Y1 = 0
                            gridLine.X2 = x : gridLine.Y2 = PlotHeight
                            gridLine.Stroke = gridBrush : gridLine.StrokeThickness = 1
                            SpatialCanvas.Children.Add(gridLine)
                            x += gridSpacing
                        End While
                        Dim y As Double = 0
                        While y <= PlotHeight
                            Dim gridLine As New Line()
                            gridLine.X1 = 0 : gridLine.Y1 = y
                            gridLine.X2 = PlotWidth : gridLine.Y2 = y
                            gridLine.Stroke = gridBrush : gridLine.StrokeThickness = 1
                            SpatialCanvas.Children.Add(gridLine)
                            y += gridSpacing
                        End While

                        ' Draw axis labels on left side (Y axis)
                        Dim axisFontSize As Double = 9
                        Dim axisForeground As New SolidColorBrush(Colors.WhiteSmoke)
                        Dim leftAxisWidth As Double = SpatialAxisLeft.ActualWidth
                        Dim labelCount As Integer = CInt(Math.Floor(PlotHeight / gridSpacing))
                        If labelCount < 2 Then labelCount = 2
                        For li As Integer = 0 To labelCount
                            Dim frac As Double = CDbl(li) / CDbl(labelCount)
                            Dim labelY As Double = PlotHeight - (frac * PlotHeight)
                            Dim axisValue As Double = If(invertY, Interpolate(frac, 0, 1, bottomVal, topVal), Interpolate(frac, 0, 1, topVal, bottomVal))
                            Dim tb As New TextBlock()
                            tb.Text = axisValue.ToString("F1")
                            tb.Foreground = axisForeground
                            tb.FontSize = axisFontSize
                            Canvas.SetRight(tb, 2)
                            Canvas.SetTop(tb, labelY - 7)
                            SpatialAxisLeft.Children.Add(tb)
                        Next

                        ' Draw axis labels on bottom side (X axis: left=min, right=max)
                        Dim bottomLabelCount As Integer = CInt(Math.Floor(PlotWidth / (gridSpacing * 3)))
                        If bottomLabelCount < 2 Then bottomLabelCount = 2
                        For li As Integer = 0 To bottomLabelCount
                            Dim frac As Double = CDbl(li) / CDbl(bottomLabelCount)
                            Dim labelX As Double = frac * PlotWidth
                            Dim axisValue As Double = Interpolate(frac, 0, 1, leftVal, rightVal)
                            Dim tb As New TextBlock()
                            tb.Text = axisValue.ToString("F1")
                            tb.Foreground = axisForeground
                            tb.FontSize = axisFontSize
                            Canvas.SetLeft(tb, labelX - 10)
                            Canvas.SetTop(tb, 2)
                            SpatialAxisBottom.Children.Add(tb)
                        Next

                        ' Draw data points as circles, or rect/size types as rectangles
                        Dim totalPoints As Integer = splice.Values.Count - 1
                        If totalPoints < 0 Then totalPoints = 0
                        For i As Integer = 0 To splice.Values.Count - 1
                            Dim v As DebugValue = splice.Values(i)
                            Dim vFlags As TypeFlags = v.Flags
                            Dim isRectType As Boolean = (vFlags.isDrawingRectangle OrElse vFlags.isShapesRect OrElse vFlags.isWindowsRect OrElse vFlags.isDrawingSize OrElse vFlags.isWindowsSize) AndAlso Not vFlags.isDrawingPoint AndAlso Not vFlags.isWindowsPoint AndAlso Not vFlags.isVector
                            Dim xVal As Double = 0
                            Dim yVal As Double = 0
                            Dim wVal As Double = 0
                            Dim hVal As Double = 0
                            If vFlags.isDrawingPoint Then
                                Dim Pt As System.Drawing.Point = DirectCast(v.Value, System.Drawing.Point)
                                xVal = Pt.X : yVal = Pt.Y
                            ElseIf vFlags.isWindowsPoint Then
                                Dim Pt As System.Windows.Point = DirectCast(v.Value, System.Windows.Point)
                                xVal = Pt.X : yVal = Pt.Y
                            ElseIf vFlags.isVector Then
                                Dim Vec As System.Windows.Vector = DirectCast(v.Value, System.Windows.Vector)
                                xVal = Vec.X : yVal = Vec.Y
                            ElseIf vFlags.isDrawingRectangle Then
                                Dim dRect As System.Drawing.Rectangle = DirectCast(v.Value, System.Drawing.Rectangle)
                                xVal = dRect.X : yVal = dRect.Y : wVal = dRect.Width : hVal = dRect.Height
                            ElseIf vFlags.isShapesRect Then
                                Dim sRect As System.Windows.Shapes.Rectangle = DirectCast(v.Value, System.Windows.Shapes.Rectangle)
                                xVal = sRect.Margin.Left : yVal = sRect.Margin.Top
                                wVal = If(Double.IsNaN(sRect.Width), 0, sRect.Width)
                                hVal = If(Double.IsNaN(sRect.Height), 0, sRect.Height)
                            ElseIf vFlags.isWindowsRect Then
                                Dim wRect As System.Windows.Rect = DirectCast(v.Value, System.Windows.Rect)
                                xVal = wRect.X : yVal = wRect.Y : wVal = wRect.Width : hVal = wRect.Height
                            ElseIf vFlags.isDrawingSize Then
                                Dim dSize As System.Drawing.Size = DirectCast(v.Value, System.Drawing.Size)
                                xVal = 0 : yVal = 0 : wVal = dSize.Width : hVal = dSize.Height
                            ElseIf vFlags.isWindowsSize Then
                                Dim wSize As System.Windows.Size = DirectCast(v.Value, System.Windows.Size)
                                xVal = 0 : yVal = 0 : wVal = wSize.Width : hVal = wSize.Height
                            End If
                            Dim mappedX As Double = Interpolate(xVal, leftVal, rightVal, 0, PlotWidth)
                            Dim mappedY As Double = If(invertY, Interpolate(yVal, topVal, bottomVal, 0, PlotHeight), PlotHeight - Interpolate(yVal, topVal, bottomVal, 0, PlotHeight))

                            ' Color: older = light blue, newest = solid purple, interpolated between
                            Dim t As Double = If(totalPoints > 0, CDbl(i) / CDbl(totalPoints), 1.0)
                            Dim r As Byte = CByte(Math.Round(Interpolate(t, 0, 1, 100, 128)))
                            Dim g As Byte = CByte(Math.Round(Interpolate(t, 0, 1, 180, 0)))
                            Dim b As Byte = CByte(Math.Round(Interpolate(t, 0, 1, 255, 255)))
                            Dim colorBrush As New SolidColorBrush(Color.FromArgb(255, r, g, b))

                            If isRectType Then
                                Dim mappedX2 As Double = Interpolate(xVal + wVal, leftVal, rightVal, 0, PlotWidth)
                                Dim mappedY2 As Double = If(invertY, Interpolate(yVal + hVal, topVal, bottomVal, 0, PlotHeight), PlotHeight - Interpolate(yVal + hVal, topVal, bottomVal, 0, PlotHeight))
                                Dim rectLeft As Double = Math.Min(mappedX, mappedX2)
                                Dim rectTop As Double = Math.Min(mappedY, mappedY2)
                                Dim rectWidth As Double = Math.Max(Math.Abs(mappedX2 - mappedX), 1)
                                Dim rectHeight As Double = Math.Max(Math.Abs(mappedY2 - mappedY), 1)
                                Dim rect As New System.Windows.Shapes.Rectangle()
                                rect.Width = rectWidth
                                rect.Height = rectHeight
                                rect.Stroke = colorBrush
                                rect.StrokeThickness = 2
                                rect.Fill = New SolidColorBrush(Color.FromArgb(40, r, g, b))
                                Canvas.SetLeft(rect, rectLeft)
                                Canvas.SetTop(rect, rectTop)
                                SpatialCanvas.Children.Add(rect)
                            Else
                                Dim circle As New Ellipse()
                                circle.Width = circleSize : circle.Height = circleSize
                                circle.Fill = colorBrush
                                Canvas.SetLeft(circle, mappedX - circleSize / 2)
                                Canvas.SetTop(circle, mappedY - circleSize / 2)
                                SpatialCanvas.Children.Add(circle)
                            End If
                        Next
                        LowLabel.Text = "X: " & leftVal.ToString("F1") & ", Y: " & topVal.ToString("F1")
                        HighLabel.Text = "X: " & rightVal.ToString("F1") & ", Y: " & bottomVal.ToString("F1")
                    End Sub)
                Return
            End If
            If Flags.isString Then
                Dim sb As New System.Text.StringBuilder()
                For i As Integer = 0 To splice.Values.Count - 1
                    Dim v As DebugValue = splice.Values(i)
                    Dim s As String = If(v.Value IsNot Nothing, CStr(v.Value), "NULL")
                    sb.AppendLine("[" & (Lower + i) & "] " & s)
                Next
                Dim text As String = sb.ToString()
                InvokeUI(
                    Sub()
                        Points.Clear()
                        SpatialCanvas.Visibility = Visibility.Collapsed
                        SpatialAxisLeft.Visibility = Visibility.Collapsed
                        SpatialAxisBottom.Visibility = Visibility.Collapsed
                        InvertSpatialCheckBox.Visibility = Visibility.Collapsed
                        p.Visibility = Visibility.Collapsed
                        LowLabel.Visibility = Visibility.Collapsed
                        HighLabel.Visibility = Visibility.Collapsed
                        StringValueTextBox.Visibility = Visibility.Visible
                        StringValueTextBox.Text = text
                        StringValueTextBox.ScrollToEnd()
                    End Sub)
                Return
            End If
            Dim newPoints As New List(Of Point)
            Dim l As Integer = splice.Values.Count - 1
            Dim pointWidth As Double = PlotWidth / If(l > 0, l, 1)
            Dim localDrawIndex As Double = 0

            newPoints.Add(New Point(localDrawIndex, PlotHeight))
            newPoints.Add(New Point(localDrawIndex, 0))
            newPoints.Add(New Point(localDrawIndex, PlotHeight))
            For i As Integer = 0 To splice.Values.Count - 1
                Dim v As DebugValue = splice.Values(i)
                Dim InterpolatedValue As Double

                If Flags.isBoolean Then
                    If v.Value Then
                        InterpolatedValue = PlotHeight
                    Else
                        InterpolatedValue = 0
                    End If
                ElseIf Flags.isNumeric Then
                    If Not CurrentTimeline.HighestValue.GetType() Is GetType(Char) Then
                        InterpolatedValue = Interpolate(v.Value, CurrentTimeline.LowestValue, CurrentTimeline.HighestValue, 0, PlotHeight)
                    End If
                End If

                newPoints.Add(New Point(localDrawIndex, PlotHeight - InterpolatedValue))
                newPoints.Add(New Point(localDrawIndex + pointWidth, PlotHeight - InterpolatedValue))

                localDrawIndex += pointWidth
            Next

            newPoints.Add(New Point(localDrawIndex, PlotHeight))

            InvokeUI(
                Sub()
                    Points.Clear()
                    SpatialCanvas.Visibility = Visibility.Collapsed
                    SpatialAxisLeft.Visibility = Visibility.Collapsed
                    SpatialAxisBottom.Visibility = Visibility.Collapsed
                    InvertSpatialCheckBox.Visibility = Visibility.Collapsed
                    StringValueTextBox.Visibility = Visibility.Collapsed
                    p.Visibility = Visibility.Visible
                    LowLabel.Visibility = Visibility.Visible
                    HighLabel.Visibility = Visibility.Visible

                    For Each pt In newPoints
                        Points.Add(pt)
                    Next
                    DrawIndex = localDrawIndex

                    If Flags.isBoolean Then
                        LowLabel.Text = "False"
                        HighLabel.Text = "True"
                    Else
                        LowLabel.Text = CurrentTimeline.LowestValue
                        HighLabel.Text = CurrentTimeline.HighestValue
                    End If
                End Sub)


        End If

    End Sub

    Public Sub CreatePoint(x As Double, y As Double)
        InvokeUI(Sub() Points.Add(New Point(x, PlotHeight - y)))
    End Sub

#End Region

#Region "Shared Properties"
    Public Shared Property MaxRange As Integer = 1000
    Public Shared Property MaxRecursiveThreads As Integer = 10
    Public Shared Property AnimationDuration As TimeSpan = TimeSpan.FromSeconds(0.75)
    Public Shared Property DefaultBackground As Color = Color.FromArgb(255, 37, 37, 37)
    Public Shared Property ValueChangedAnimation As New ColorAnimation(Colors.LimeGreen, DefaultBackground, AnimationDuration)
    Public Shared Property SeparatorBrush As New SolidColorBrush(Colors.LightGray)

#End Region

#Region "Properties"

    Public Property KeepAtEnd As Boolean = False
    Private Property DrawIndex As Double = 0

    Public ReadOnly Property isAtEnd As Boolean
        Get
            If Not isUserChangingUpper AndAlso Upper >= Maximum * 0.95 Then
                KeepAtEnd = True
            End If
            Return Upper = Maximum
        End Get
    End Property

    Public ReadOnly Property isAtBeginning As Boolean
        Get
            Return Lower = 0
        End Get
    End Property

    Public Property Enabled As Boolean
        Get
            Return _Enabled
        End Get
        Set(value As Boolean)
            If _Enabled = value Then Return
            _Enabled = value
            Dim watcherSnapshot As List(Of DebugWatcher)
            SyncLock DebugWatcher.Watchers
                watcherSnapshot = DebugWatcher.Watchers.Values.ToList()
            End SyncLock
            For Each w In watcherSnapshot
                w.isEnabled = value
            Next
            If value Then
                EnsureBackgroundWorker()
            End If
        End Set
    End Property
    Private _Enabled As Boolean = False

    Public Property Realtime As Boolean
        Get
            Return _Realtime
        End Get
        Set(value As Boolean)
            _Realtime = value
        End Set
    End Property
    Private _Realtime As Boolean = True

    Public ReadOnly Property PlotHeight As Double
        Get
            Return _PlotHeight
        End Get
    End Property
    Private _PlotHeight As Double = 1

    Public ReadOnly Property PlotWidth As Double
        Get
            Return _PlotWidth
        End Get
    End Property
    Private _PlotWidth As Double = 1

    Public Property Upper As Integer
        Get
            Return _Upper
        End Get
        Set(value As Integer)
            Application.Current.Dispatcher.Invoke(Sub() Slider.UpperValue = value)
        End Set
    End Property
    Private _Upper As Integer

    Public Property Lower As Integer
        Get
            Return _Lower
        End Get
        Set(value As Integer)
            Application.Current.Dispatcher.Invoke(Sub() Slider.LowerValue = value)
        End Set
    End Property
    Private _Lower As Integer

    Public Property Maximum As Integer
        Get
            Return _Maximum
        End Get
        Set(value As Integer)
            _Maximum = value
            Application.Current.Dispatcher.Invoke(Sub() Slider.Maximum = value)
        End Set
    End Property
    Private _Maximum As Integer

    Public ReadOnly Property WatcherCount As Integer
        Get
            SyncLock DebugWatcher.Watchers
                Return DebugWatcher.Watchers.Count
            End SyncLock
        End Get
    End Property
    Private _WatcherCount As Integer = 0

    Public ReadOnly Property TimelineDifference As Integer
        Get
            Return _TimelineDifference
        End Get
    End Property
    Private _TimelineDifference As Integer = 0

    Public ReadOnly Property IsMaxWidth As Boolean
        Get
            Return Upper - Lower > MaxRange
        End Get
    End Property

#End Region

#Region "Tree View"

    Private TreeItems As New Dictionary(Of String, TreeViewItem)

    Private Async Function CreateTreeItem(Type As Type, Header As String, TreeItemGuid As String, Optional ToolTip As String = Nothing) As Task(Of TreeViewItem)
        Return Await ReturnValueUI(Function()
                                       Dim nv = New TreeViewItem
                                       nv.Tag = TreeItemGuid
                                       If ToolTip = Nothing Then ToolTip = Type.Name
                                       With nv
                                           .ToolTip = ToolTip
                                           .Header = Header
                                           .Background = New SolidColorBrush(DefaultBackground)
                                       End With
                                       Return nv
                                   End Function)
    End Function

#End Region

#Region "Animations"

    Public Sub ValueChangedAnim(TreeViewItem As TreeViewItem)
        InvokeUI(Sub() TreeViewItem.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation))
    End Sub

    Public Sub ValueChangedAnim(TreeViewItem As TreeViewItem, Indexies As List(Of Integer))
        InvokeUI(
            Sub()
                If Indexies IsNot Nothing AndAlso Indexies.Count > 0 Then
                    For i As Integer = 0 To Indexies.Count - 1
                        If TreeViewItem.Items.Count >= i Then
                            Try
                                Dim aTVI As TreeViewItem = TreeViewItem.Items(i)
                                aTVI.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation)
                            Catch ex As Exception

                            End Try
                        End If
                    Next
                End If
                TreeViewItem.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation)
            End Sub)
    End Sub

#End Region

#Region "Functions"

    Private Sub SetTimeline(Guid As String)
        ClearPoints()
        CurrentTimeline = Nothing
        KeepAtEnd = True
        If Guid Is Nothing Then Return
        If ValueTimeline.Timelines.ContainsKey(Guid) Then
            CurrentTimeline = ValueTimeline.Timelines(Guid)
            _lastRenderSignature = ""
            DrawTimeline()
        End If
    End Sub

    Private Sub ShowAddWatchButton()
        InvokeUI(Sub()
                     AddWatchButton.Visibility = Visibility.Visible
                     p.Visibility = Visibility.Collapsed
                     StringValueTextBox.Visibility = Visibility.Collapsed
                     LowLabel.Visibility = Visibility.Collapsed
                     HighLabel.Visibility = Visibility.Collapsed
                 End Sub)
    End Sub

    Private Sub ShowValueActions(value As DebugValue, watcher As DebugWatcher)
        ValueActions.Visibility = Visibility.Visible
        AddWatchButton.Visibility = If(value.Type IsNot Nothing AndAlso Not value.Type.IsValueType AndAlso Not value.Flags.isString AndAlso Not watcher.ChildWatcherValues.Contains(value.Guid), Visibility.Visible, Visibility.Collapsed)
        ApplyValueButton.Visibility = If(CanEdit(value), Visibility.Visible, Visibility.Collapsed)
        EditValueTextBox.Visibility = ApplyValueButton.Visibility
        Dim backingField As FieldInfo = Nothing
        If value.IsProperty AndAlso value.PropertyReference IsNot Nothing Then
            backingField = FindUnderscoreBackingField(value.PropertyReference.Info, watcher.AttachedObject)
        End If
        SetBackingFieldToggle.Visibility = If(ApplyValueButton.Visibility = Visibility.Visible AndAlso backingField IsNot Nothing, Visibility.Visible, Visibility.Collapsed)
        SetBackingFieldToggle.Header = If(backingField Is Nothing, "Also set backing field", "Also set " & backingField.Name)
        SetBackingFieldToggle.IsOn = False
        EditValueTextBox.Text = If(value.Value Is Nothing, "", Convert.ToString(value.Value, CultureInfo.InvariantCulture))
        EditStatus.Text = ""
    End Sub

    Private Function CanEdit(value As DebugValue) As Boolean
        Return value IsNot Nothing AndAlso ((value.IsField AndAlso value.FieldReference IsNot Nothing AndAlso Not value.FieldReference.Info.IsInitOnly) OrElse (value.IsProperty AndAlso value.PropertyReference IsNot Nothing AndAlso value.PropertyReference.Info.CanWrite))
    End Function

    Private Shared Sub InvokePropertySetter(propertyInfo As Reflection.PropertyInfo, instance As Object, convertedValue As Object)
        Dim setter = propertyInfo.GetSetMethod(True)
        If setter Is Nothing Then
            Throw New InvalidOperationException("The selected property does not expose a setter.")
        End If

        setter.Invoke(If(setter.IsStatic, Nothing, instance), New Object() {convertedValue})
    End Sub

    Private Shared Function FindUnderscoreBackingField(propertyInfo As PropertyInfo, instance As Object) As FieldInfo
        If propertyInfo Is Nothing Then Return Nothing
        Dim setter = propertyInfo.GetSetMethod(True)
        If setter Is Nothing Then Return Nothing

        Dim flags = BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.IgnoreCase Or BindingFlags.DeclaredOnly Or
                    If(setter.IsStatic, BindingFlags.Static, BindingFlags.Instance)
        Dim targetType = If(instance Is Nothing, propertyInfo.DeclaringType, instance.GetType())
        While targetType IsNot Nothing
            Dim candidate = targetType.GetField("_" & propertyInfo.Name, flags)
            If candidate IsNot Nothing AndAlso Not candidate.IsInitOnly AndAlso Not candidate.IsLiteral AndAlso
               candidate.IsStatic = setter.IsStatic AndAlso candidate.FieldType.IsAssignableFrom(propertyInfo.PropertyType) Then
                Return candidate
            End If
            targetType = targetType.BaseType
        End While
        Return Nothing
    End Function

    Private Sub ApplyValueButton_Click(sender As Object, e As RoutedEventArgs) Handles ApplyValueButton.Click
        If _selectedValueGuid Is Nothing OrElse Not ValueOwnerLookup.ContainsKey(_selectedValueGuid) Then Return
        Dim info = ValueOwnerLookup(_selectedValueGuid)
        Dim value = info.Item1
        Dim watcher = info.Item2
        Try
            Dim targetType = value.Type
            Dim conversionType = If(Nullable.GetUnderlyingType(targetType), targetType)
            Dim converted As Object = If(targetType Is GetType(String), EditValueTextBox.Text, If(conversionType.IsEnum, [Enum].Parse(conversionType, EditValueTextBox.Text, True), Convert.ChangeType(EditValueTextBox.Text, conversionType, CultureInfo.InvariantCulture)))
            Dim appliedBackingField As FieldInfo = Nothing
            Dim apply As Action = Sub()
                                      If value.IsField Then
                                          value.FieldReference.Info.SetValue(If(value.FieldReference.Info.IsStatic, Nothing, watcher.AttachedObject), converted)
                                          Return
                                      End If

                                      ' Invoke the property's accessor directly so applying a live value
                                      ' executes all logic implemented by the real setter.
                                      InvokePropertySetter(value.PropertyReference.Info, watcher.AttachedObject, converted)
                                      If SetBackingFieldToggle.Visibility = Visibility.Visible AndAlso SetBackingFieldToggle.IsOn Then
                                          appliedBackingField = FindUnderscoreBackingField(value.PropertyReference.Info, watcher.AttachedObject)
                                          If appliedBackingField Is Nothing Then Throw New InvalidOperationException("The matching underscore backing field is no longer available.")
                                          appliedBackingField.SetValue(If(appliedBackingField.IsStatic, Nothing, watcher.AttachedObject), converted)
                                      End If
                                  End Sub
            If TypeOf watcher.AttachedObject Is System.Windows.Threading.DispatcherObject Then DirectCast(watcher.AttachedObject, System.Windows.Threading.DispatcherObject).Dispatcher.Invoke(apply) Else apply()
            EditStatus.Text = If(appliedBackingField Is Nothing, "Applied.", "Applied property and " & appliedBackingField.Name & ".")
        Catch ex As Exception
            EditStatus.Text = ex.Message
        End Try
    End Sub

    Private Sub HideAddWatchButton()
        InvokeUI(Sub()
                     AddWatchButton.Visibility = Visibility.Collapsed
                     ValueActions.Visibility = Visibility.Collapsed
                     p.Visibility = Visibility.Visible
                     StringValueTextBox.Visibility = Visibility.Collapsed
                     LowLabel.Visibility = Visibility.Visible
                     HighLabel.Visibility = Visibility.Visible
                 End Sub)
    End Sub

    Private Sub AddWatchButton_Click(sender As Object, e As RoutedEventArgs) Handles AddWatchButton.Click
        If _selectedValueGuid Is Nothing Then Return
        If Not ValueOwnerLookup.ContainsKey(_selectedValueGuid) Then Return

        Dim info = ValueOwnerLookup(_selectedValueGuid)
        Dim val = info.Item1
        Dim watcher = info.Item2

        Dim actualValue As Object = Nothing
        Try
            If val.IsField AndAlso val.FieldReference IsNot Nothing Then
                actualValue = val.FieldReference.Info.GetValue(watcher.AttachedObject)
            ElseIf val.IsProperty AndAlso val.PropertyReference IsNot Nothing Then
                actualValue = val.PropertyReference.Info.GetValue(watcher.AttachedObject)
            End If
        Catch
            Return
        End Try

        If actualValue Is Nothing Then Return

        Dim child = DebugWatcher.CreateChild(watcher, _selectedValueGuid, actualValue, False)
        If child Is Nothing Then
            EditStatus.Text = "Watch limit reached, duplicate/cyclic object, or maximum depth reached."
            Return
        End If
        val.Flags.isChild = True
        _selectedValueGuid = Nothing
        HideAddWatchButton()
    End Sub

    Private Function SubItemSelected(TVI As TreeViewItem) As Boolean
        For i As Integer = 0 To TVI.Items.Count - 1
            If TVI.Items(i).GetType IsNot GetType(TreeViewItem) Then Continue For
            Dim item As TreeViewItem = TVI.Items(i)
            If item.IsSelected Then
                Return True
            End If
        Next
        Return False
    End Function

#End Region

#Region "Background Worker"

    Private WithEvents BackgroundWorker As Timer
    Private ReadOnly BackgroundWorkerGate As New Object()

    Private Sub EnsureBackgroundWorker()
        SyncLock BackgroundWorkerGate
            If BackgroundWorker Is Nothing AndAlso Enabled Then
                BackgroundWorker = New Timer(New TimerCallback(AddressOf BackgroundWorker_DoWork), Nothing, 0, Timeout.Infinite)
            End If
        End SyncLock
    End Sub
    ''' <summary>
    ''' Tree View Item Updates
    ''' </summary>
    Private Async Sub BackgroundWorker_DoWork(state As Object)
        Do While Enabled
            Dim watcherSnapshot As List(Of DebugWatcher)
            SyncLock DebugWatcher.Watchers
                watcherSnapshot = DebugWatcher.Watchers.Values.ToList()
            End SyncLock
            Dim nCount As Integer = watcherSnapshot.Count
            If nCount <> InitializedWatchers.Count Then
                For Each w As DebugWatcher In watcherSnapshot
                    If Not Enabled Then Exit For

                    Dim t As Type = w.TargetType

                    Dim WatcherInitialized As Boolean = InitializedWatchers.ContainsKey(w.Guid)
                    If Not WatcherInitialized Then

                        If w.isChild Then
                            Dim ParentInitialized As Boolean = InitializedWatchers.ContainsKey(w.Parent.Guid)
                            Dim ParentValueInitialized As Boolean = TreeItems.ContainsKey(w.ParentValueGuid)
                            If ParentInitialized And ParentValueInitialized Then
                                InvokeUI(Sub()
                                             If InitializedWatchers.ContainsKey(w.Guid) Then Return
                                             InitializedWatchers.Add(w.Guid, w)
                                             AddHandler w.ValueCalculated, AddressOf ValueCalculated
                                             AddHandler w.ValueChanged, AddressOf ValueChanged
                                             Try
                                                 w.PublishCurrentValues()
                                             Catch ex As Exception
                                                 TargetStatusText.Text = "Could not read " & w.Name & ": " & ex.GetBaseException().Message
                                             End Try
                                          End Sub)
                            End If
                        Else
                            Dim NewWatcher As TreeViewItem = Await CreateTreeItem(t, w.Name, w.Guid)
                            InvokeUI(Sub()
                                         If InitializedWatchers.ContainsKey(w.Guid) Then Return
                                         InitializedWatchers.Add(w.Guid, w)
                                         Watchers.Items.Add(NewWatcher)
                                          TreeItems.Add(w.Guid, NewWatcher)
                                          AddHandler w.ValueCalculated, AddressOf ValueCalculated
                                          AddHandler w.ValueChanged, AddressOf ValueChanged
                                          Try
                                              w.PublishCurrentValues()
                                          Catch ex As Exception
                                              TargetStatusText.Text = "Could not read " & w.Name & ": " & ex.GetBaseException().Message
                                          End Try
                                      End Sub)
                        End If
                    End If
                Next
                _WatcherCount = nCount

            End If
            If NotNothing(CurrentTimeline) Then
                DrawTimeline()
            Else
                ClearPoints()
            End If
            Await Task.Delay(If(Interval > MinimumInterval, Interval, MinimumInterval))
        Loop
        Dim shouldRestart As Boolean
        SyncLock BackgroundWorkerGate
            Dim completedWorker = BackgroundWorker
            BackgroundWorker = Nothing
            If completedWorker IsNot Nothing Then completedWorker.Dispose()
            shouldRestart = Enabled
        End SyncLock
        If shouldRestart Then EnsureBackgroundWorker()
    End Sub

#End Region

    Private Sub ValueChanged(Watcher As DebugWatcher, Value As DebugValue, ArrayIndexies As List(Of Integer))
        If Not Dispatcher.CheckAccess() Then
            Dispatcher.BeginInvoke(Sub() ValueChanged(Watcher, Value, ArrayIndexies))
            Return
        End If
        If ContainsTVI(Value) Then
            If Value.Flags.isCollection Then
                UpdateCollectionTVI(Watcher, Value)
            Else
                UpdateSingleTVI(Watcher, Value)
            End If
        End If
        IterateIndex(Value)
    End Sub

    Private Sub ValueCalculated(Watcher As DebugWatcher, Value As DebugValue)
        If Not Dispatcher.CheckAccess() Then
            Dispatcher.BeginInvoke(Sub() ValueCalculated(Watcher, Value))
            Return
        End If
        If Value Is Nothing Then Return
        If Not ValueOwnerLookup.ContainsKey(Value.Guid) Then
            ValueOwnerLookup.Add(Value.Guid, Tuple.Create(Value, Watcher))
        End If
        If Not ContainsTVI(Value) Then
            If Value.Flags.isCollection Then
                CreateCollectionTVI(Watcher, Value)
            Else
                CreateSingleTVI(Watcher, Value)
            End If
        End If
        IterateIndex(Value)
    End Sub

    Private Sub UpdateHeader(TVI As TreeViewItem, Header As String, Optional p As TreeViewItem = Nothing)
        Try
            Dim ValueChanged As Boolean = TVI.Header <> Header
            If ValueChanged Then ValueChangedAnim(TVI)
            TVI.Header = Header
            If p IsNot Nothing AndAlso ValueChanged Then
                ValueChangedAnim(p)
            End If
        Catch : End Try
    End Sub

    Private Async Sub UpdateArrayTVI(TVI As TreeViewItem, Value As DebugValue)
        Dim ArrayType As Type = Nothing
        For i As Integer = 0 To Value.Length - 1
            Dim Index As Integer = i
            Try
                Dim ArrayItem As Object = Value.Value(Index)
                If ArrayType Is Nothing AndAlso Not IsNothing(ArrayItem) Then ArrayType = ArrayItem.GetType()
                Dim ValueToString As String = If(IsNothing(ArrayItem), "NULL", Me.ValueToString(ArrayItem.ToString()))
                Dim Header As String = "[" & Index & "] " & ValueToString
                Dim ItemCount1 As Integer = Await ReturnValueUI(Function() TVI.Items.Count - 1)
                If Index > ItemCount1 Then
                    Dim aTVI As TreeViewItem = Await CreateTreeItem(ArrayType, Header, Nothing)
                    InvokeUI(
                        Sub()
                            If Index > Value.Value.Length - 1 Then Return
                            If TVI.Parent Is Nothing Then Return
                            Try : TVI.Items.Add(aTVI) : ValueChangedAnim(aTVI) : Catch : End Try
                        End Sub)
                Else
                    InvokeUI(
                        Sub()
                            If Index > Value.Value.length - 1 Then Return
                            If TVI.Parent Is Nothing Then Return
                            Try : UpdateHeader(TVI.Items(Index), Header, TVI) : Catch : End Try
                        End Sub)
                End If
                If Not IsNothing(ArrayItem) AndAlso Not IsSystemType(ArrayItem.GetType()) Then
                    Dim elementTVI As TreeViewItem = Nothing
                    InvokeUI(Sub()
                                 If Index <= TVI.Items.Count - 1 Then
                                     Try : elementTVI = DirectCast(TVI.Items(Index), TreeViewItem) : Catch : End Try
                                 End If
                             End Sub)
                    If elementTVI IsNot Nothing Then UpdateItemProperties(elementTVI, ArrayItem)
                End If
            Catch
            End Try
        Next
    End Sub

    Private Async Sub UpdateListTVI(TVI As TreeViewItem, Value As DebugValue)
        Dim ArrayType As Type = Nothing
        Dim L As Integer = Value.Length - 1
        For i As Integer = 0 To L
            Try
                If i > Value.Value.Count - 1 Then Exit For
                Dim Index As Integer = i
                Dim ArrayItem As Object = Value.Value(Index)
                Dim ValueToString As String = If(IsNothing(ArrayItem), "NULL", Me.ValueToString(ArrayItem.ToString()))
                If ArrayType Is Nothing AndAlso Not IsNothing(ArrayItem) Then ArrayType = ArrayItem.GetType()
                Dim TypeName As String = If(ArrayType IsNot Nothing, ArrayType.Name, "Object")
                Dim Header As String = "[" & Index & "] " & TypeName & ": " & ValueToString
                If Index > TVI.Items.Count - 1 Then
                    Dim aTVI As TreeViewItem = Await CreateTreeItem(If(ArrayType, GetType(Object)), Header, Nothing)
                    InvokeUI(
                    Sub()
                        If Index > Value.Value.Count - 1 Then Return
                        If TVI.Parent Is Nothing Then Return
                        Try : TVI.Items.Add(aTVI) : ValueChangedAnim(TVI) : ValueChangedAnim(aTVI) : Catch : End Try
                    End Sub)
                Else
                    InvokeUI(
                    Sub()
                        If Index > Value.Value.Count - 1 Then Return
                        If TVI.Parent Is Nothing Then Return
                        Try : UpdateHeader(TVI.Items(Index), Header, TVI) : Catch : End Try
                    End Sub)
                End If
                If Not IsNothing(ArrayItem) AndAlso Not IsSystemType(ArrayItem.GetType()) Then
                    Dim elementTVI As TreeViewItem = Nothing
                    InvokeUI(Sub()
                                 If Index <= TVI.Items.Count - 1 Then
                                     Try : elementTVI = DirectCast(TVI.Items(Index), TreeViewItem) : Catch : End Try
                                 End If
                             End Sub)
                    If elementTVI IsNot Nothing Then UpdateItemProperties(elementTVI, ArrayItem)
                End If
            Catch
            End Try
        Next
    End Sub

    Private Sub UpdateDictionaryTVI(TVI As TreeViewItem, Value As DebugValue)
        Dim keys As New List(Of Object)
        Dim values As New List(Of Object)
        Try
            Dim keyList As IList = Value.KeyList
            Dim valueList As IList = Value.ValueList
            If keyList Is Nothing OrElse valueList Is Nothing Then Return
            Dim count As Integer = Math.Min(keyList.Count, valueList.Count)
            For i As Integer = 0 To count - 1
                keys.Add(keyList(i))
                values.Add(valueList(i))
            Next
        Catch
            Return
        End Try

        Dim ArrayType As Type = Nothing
        For i As Integer = 0 To keys.Count - 1
            Dim Index As Integer = i
            Dim ArrayItemKey As Object = keys(Index)
            Dim ArrayItemValue As Object = values(Index)
            Dim ValueToString As String = If(IsNothing(ArrayItemValue), "NULL", Me.ValueToString(ArrayItemValue.ToString()))
            If ArrayType Is Nothing AndAlso Not IsNothing(ArrayItemValue) Then ArrayType = ArrayItemValue.GetType()
            Dim TypeName As String = If(ArrayType IsNot Nothing, ArrayType.Name, "Object")
            Dim Header As String = "[" & Index & "]" & ArrayItemKey.ToString() & ": " & ValueToString
            InvokeUI(Async Sub()
                         If Index > TVI.Items.Count - 1 Then
                             Dim aTVI As TreeViewItem = Await CreateTreeItem(If(ArrayType, GetType(Object)), Header, Nothing)
                             If TVI.Parent Is Nothing Then Return
                             Try : TVI.Items.Add(aTVI) : ValueChangedAnim(TVI) : ValueChangedAnim(aTVI) : Catch : End Try
                         Else

                             If TVI.Parent Is Nothing Then Return
                             If Index > TVI.Items.Count - 1 Then Return
                             Try : UpdateHeader(TVI.Items(Index), Header, TVI) : Catch : End Try
                         End If
                     End Sub)

            If Not IsNothing(ArrayItemValue) AndAlso Not IsSystemType(ArrayItemValue.GetType()) Then
                Dim elementTVI As TreeViewItem = Nothing
                InvokeUI(Sub()
                             If Index <= TVI.Items.Count - 1 Then
                                 Try : elementTVI = DirectCast(TVI.Items(Index), TreeViewItem) : Catch : End Try
                             End If
                         End Sub)
                If elementTVI IsNot Nothing Then UpdateItemProperties(elementTVI, ArrayItemValue)
            End If
        Next
    End Sub

    Private Class NestedMember
        Public Owner As Object
        Public ReadOnly Member As MemberInfo
        Public ReadOnly Timeline As ValueTimeline
        Public Sub New(instance As Object, memberInfo As MemberInfo)
            Owner = instance
            Member = memberInfo
            Timeline = New ValueTimeline(System.Guid.NewGuid().ToString())
        End Sub
    End Class

    Private SelectedNestedMember As NestedMember
    Private WithEvents NestedSampleTimer As New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(100)}
    Private NestedSamplePending As Boolean

    Private Async Sub SampleNestedMember(sender As Object, e As EventArgs) Handles NestedSampleTimer.Tick
        Dim target = SelectedNestedMember
        If target Is Nothing OrElse Not Enabled OrElse NestedSamplePending Then Return
        NestedSamplePending = True
        Try
            Dim owner = target.Owner
            ' At most one evaluation is in flight, even if a target getter blocks.
            Dim value = Await Task.Run(Function()
                                          Dim field = TryCast(target.Member, FieldInfo)
                                          Dim prop = TryCast(target.Member, PropertyInfo)
                                          Dim raw = If(field IsNot Nothing, field.GetValue(owner), prop.GetValue(owner))
                                          Dim valueType = If(field IsNot Nothing, field.FieldType, prop.PropertyType)
                                          Return New DebugValue(target.Member.Name, Nothing, Nothing, valueType,
                                              DebugValue.GetTypeFlags(New SafeValue(raw), valueType), raw)
                                      End Function)
            If SelectedNestedMember IsNot target Then Return
            value.Guid = target.Timeline.GUID
            target.Timeline.AddValue(value)
            DrawTimeline()
        Catch ex As Exception
            If SelectedNestedMember Is target Then
                ShowEmptyVisualization("Cannot sample " & target.Member.Name & ": " & ex.GetBaseException().Message)
                NestedSampleTimer.Stop()
            End If
        Finally
            NestedSamplePending = False
        End Try
    End Sub

    Private Sub UpdateItemProperties(aTVI As TreeViewItem, item As Object)
        If item Is Nothing Then Return

        Dim itemType As Type = item.GetType()
        If itemType.IsPrimitive OrElse itemType.IsEnum OrElse itemType Is GetType(String) OrElse
           GetType(System.Delegate).IsAssignableFrom(itemType) Then Return

        Dim headerTexts As New List(Of String)
        Dim toolTips As New List(Of String)
        Dim members As New List(Of MemberInfo)
        Try
            For Each f As FieldInfo In itemType.GetFields(BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic).Take(6)
                If DebugValue.IgnoreTypes.Contains(f.FieldType) Then Continue For
                Try
                    Dim fVal As Object = f.GetValue(item)
                    Dim valueStr As String = If(IsNothing(fVal), "NULL", Me.ValueToString(fVal.ToString()))
                    headerTexts.Add(f.Name & ": " & valueStr)
                    toolTips.Add(f.FieldType.Name)
                    members.Add(f)
                Catch
                End Try
            Next

            For Each p As PropertyInfo In itemType.GetProperties(BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic).Take(Math.Max(0, 12 - headerTexts.Count))
                If Not p.CanRead Then Continue For
                If p.GetIndexParameters().Length > 0 Then Continue For
                If DebugValue.IgnoreTypes.Contains(p.PropertyType) Then Continue For
                Try
                    Dim pVal As Object = p.GetValue(item)
                    Dim valueStr As String = If(IsNothing(pVal), "NULL", Me.ValueToString(pVal.ToString()))
                    headerTexts.Add(p.Name & ": " & valueStr)
                    toolTips.Add(p.PropertyType.Name)
                    members.Add(p)
                Catch
                End Try
            Next
        Catch
            Return
        End Try

        If headerTexts.Count = 0 Then Return

        InvokeUI(Sub()
                     Try
                         For idx As Integer = 0 To headerTexts.Count - 1
                             If idx > aTVI.Items.Count - 1 Then
                                 Dim childTVI As New TreeViewItem()
                                 childTVI.Header = headerTexts(idx)
                                 childTVI.ToolTip = toolTips(idx)
                                 childTVI.Tag = New NestedMember(item, members(idx))
                                 childTVI.Background = New SolidColorBrush(DefaultBackground)
                                 aTVI.Items.Add(childTVI)
                                 ValueChangedAnim(childTVI)
                             Else
                                 UpdateHeader(DirectCast(aTVI.Items(idx), TreeViewItem), headerTexts(idx))
                                 Dim node = DirectCast(aTVI.Items(idx), TreeViewItem)
                                 Dim nested = TryCast(node.Tag, NestedMember)
                                 If nested IsNot Nothing AndAlso nested.Member.Equals(members(idx)) Then
                                     nested.Owner = item
                                 Else
                                     node.Tag = New NestedMember(item, members(idx))
                                 End If
                             End If
                         Next

                         While aTVI.Items.Count > headerTexts.Count
                             aTVI.Items.RemoveAt(aTVI.Items.Count - 1)
                         End While
                         If headerTexts.Count <= 12 Then aTVI.IsExpanded = True
                     Catch
                     End Try
                 End Sub)
    End Sub

    Private Sub PrepareInspectableNode(item As TreeViewItem, value As DebugValue)
        If item Is Nothing OrElse value Is Nothing OrElse value.Value Is Nothing OrElse value.Type Is Nothing OrElse
           value.Type.IsPrimitive OrElse value.Type.IsEnum OrElse value.Type Is GetType(String) Then Return
        If value.Type.IsValueType Then
            UpdateItemProperties(item, value.Value)
            Return
        End If

        If item.Items.Count = 0 Then
            item.Items.Add(New TreeViewItem With {
                .Header = "Expand to inspect current fields and properties...",
                .Foreground = New SolidColorBrush(Color.FromRgb(130, 143, 136)),
                .IsHitTestVisible = False
            })
        End If
        RemoveHandler item.Expanded, AddressOf InspectableTreeItem_Expanded
        AddHandler item.Expanded, AddressOf InspectableTreeItem_Expanded
    End Sub

    Private Sub InspectableTreeItem_Expanded(sender As Object, e As RoutedEventArgs)
        Dim item = TryCast(sender, TreeViewItem)
        If item Is Nothing OrElse Not Object.ReferenceEquals(e.OriginalSource, item) Then Return
        Dim guid = TryCast(item.Tag, String)
        If guid Is Nothing OrElse Not ValueOwnerLookup.ContainsKey(guid) Then Return
        Dim value = ValueOwnerLookup(guid).Item1
        UpdateItemProperties(item, value.Value)
    End Sub

    Private Async Sub PruneCollection(TVI As TreeViewItem, Value As DebugValue)
        If Value.Length <= 0 AndAlso Not Value.Flags.isChild Then
            InvokeUI(Sub() TVI.Items.Clear())
            Return
        End If
        Dim ItemCount As Integer = Await ReturnValueUI(Function() TVI.Items.Count)
        If ItemCount > Value.Length Then
            For x As Integer = ItemCount - 1 To Value.Length Step -1
                Dim Index As Integer = x
                InvokeUI(Sub()
                             If TVI.Items.Count > Index Then
                                 Try : TVI.Items.RemoveAt(Index) : Catch : End Try
                             End If
                         End Sub)
            Next
        End If
    End Sub

    Private Sub UpdateCollectionTVI(Watcher As DebugWatcher, Value As DebugValue)
        Dim TVI As TreeViewItem = GetTVI(Value)
        If Value.Flags.isDictionary AndAlso Value.KeyList IsNot Nothing Then
            UpdateDictionaryTVI(TVI, Value)
        ElseIf Value.Flags.isList AndAlso Value.Value IsNot Nothing Then
            UpdateListTVI(TVI, Value)
        ElseIf Value.Flags.isArray AndAlso Value.Value IsNot Nothing Then
            UpdateArrayTVI(TVI, Value)
        End If
        PruneCollection(TVI, Value)
    End Sub

    Private Sub UpdateSingleTVI(Watcher As DebugWatcher, Value As DebugValue)
        If ContainsTVI(Value) Then
            Dim ValueToString As String = If(IsNothing(Value.Value), "NULL", Me.ValueToString(Value.Value.ToString()))
            If Value.Flags.isString Then
                InvokeUI(Sub()
                             Dim TVI = GetTVI(Value)
                             Dim sp = TryCast(TVI.Header, StackPanel)
                             If sp IsNot Nothing AndAlso sp.Children.Count > 1 Then
                                 Dim tb = TryCast(sp.Children(1), TextBox)
                                 If tb IsNot Nothing AndAlso tb.Text <> ValueToString Then
                                     tb.Text = ValueToString
                                     ValueChangedAnim(TVI)
                                 End If
                             End If
                         End Sub)
            Else
                Dim Header As String = Value.Name & vbCrLf & "    " & ValueToString
                InvokeUI(Sub()
                             Dim item As TreeViewItem = GetTVI(Value)
                             UpdateHeader(item, Header)
                             If Not Value.Flags.isChild Then
                                 If Value.Type IsNot Nothing AndAlso Value.Type.IsValueType Then
                                     UpdateItemProperties(item, Value.Value)
                                 ElseIf item.IsExpanded Then
                                     UpdateItemProperties(item, Value.Value)
                                 End If
                             End If
                         End Sub)
            End If
        End If
    End Sub

    Private Sub IterateIndex(Value As DebugValue)
        If CurrentTimeline IsNot Nothing AndAlso Value.Guid = CurrentTimeline.GUID Then _TimelineDifference += 1
    End Sub

    Private Function GetTVI(Value As DebugValue) As TreeViewItem
        Return TreeItems(Value.Guid)
    End Function

    Private Function GetTVI(Watcher As DebugWatcher) As TreeViewItem
        Return TreeItems(Watcher.Guid)
    End Function

    Private Function ContainsTVI(Value As DebugValue) As Boolean
        Return TreeItems.ContainsKey(Value.Guid)
    End Function

    Private Function ContainsTVI(Watcher As DebugWatcher) As Boolean
        Return TreeItems.ContainsKey(Watcher.Guid)
    End Function

    Private Function ContainsTVI(Guid As String) As Boolean
        Return TreeItems.ContainsKey(Guid)
    End Function

    Private Async Sub CreateCollectionTVI(Watcher As DebugWatcher, Value As DebugValue)
        Dim TVI As TreeViewItem = Await CreateTreeItem(Value.Type, Value.Name, Value.Guid)

        Dim ArrayType As Type = Nothing
        Dim L As Integer = Value.Length - 1
        For i As Integer = 0 To L
            Dim index As Integer = i
            If Value.Flags.isDictionary Then
                If index > Value.Value.Count - 1 Then Exit For
                ArrayType = DirectCast(Value.Value, IEnumerable).Cast(Of Object)().ElementAt(0).GetType()
            ElseIf Value.Flags.isList Then
                If index > Value.Value.Count - 1 Then Exit For

                ArrayType = Value.Value(index).GetType() ' Value.Type.GetGenericArguments()(0)
            ElseIf Value.Flags.isArray Then
                If index > Value.Value.Length - 1 Then Exit For
                ArrayType = Value.Type.GetElementType()
            End If



            InvokeUI(Sub()
                         If Value.Flags.isDictionary Then
                             If index > Value.Value.Count - 1 Then Return
                         ElseIf Value.Flags.isList Then
                             If index > Value.Value.Count - 1 Then Return
                         ElseIf Value.Flags.isArray Then
                             If index > Value.Value.Length - 1 Then Return
                         End If

                     End Sub)
            Dim Header As String
            If Value.Flags.isDictionary Then
                Dim ArrayItemKey As Object = If(index > Value.KeyList.Count - 1, "NULL", Value.KeyList(index))
                Dim ArrayItemValue As Object = If(index > Value.ValueList.Count - 1, Nothing, Value.ValueList(index))
                Dim ValueToString As String = If(IsNothing(ArrayItemValue), "NULL", Me.ValueToString(ArrayItemValue.ToString()))
                Header = "[" & index & "] " & ArrayItemKey.ToString() & ": " & ValueToString
            Else
                Dim ArrayItem As Object = Value.Value(index)
                Dim ValueToString As String = If(IsNothing(ArrayItem), "NULL", Me.ValueToString(ArrayItem.ToString()))
                Header = "[" & index & "] " & ValueToString
            End If
            Dim aTVI As TreeViewItem = Await CreateTreeItem(ArrayType, Header, Nothing)
            InvokeUI(Sub()
                         Try : TVI.Items.Add(aTVI) : Catch : End Try
                     End Sub)

        Next
        AddTVI(TVI, Watcher, Value)

    End Sub

    Private Async Sub CreateSingleTVI(Watcher As DebugWatcher, Value As DebugValue)
        Dim ValueToString As String = If(IsNothing(Value.Value), "NULL", Me.ValueToString(Value.Value))
        If Value.Flags.isString Then
            Dim TVI As TreeViewItem = Await CreateTreeItem(Value.Type, Value.Name, Value.Guid, Nothing)
            InvokeUI(Sub()
                         Dim sp As New StackPanel()
                         sp.Orientation = Orientation.Vertical
                         Dim nameBlock As New TextBlock()
                         nameBlock.Text = Value.Name
                         nameBlock.Foreground = New SolidColorBrush(Colors.White)
                         sp.Children.Add(nameBlock)
                         Dim tb As New TextBox()
                         tb.Text = ValueToString
                         tb.IsReadOnly = True
                         tb.TextWrapping = TextWrapping.Wrap
                         tb.Background = New SolidColorBrush(DefaultBackground)
                         tb.Foreground = New SolidColorBrush(Colors.LightGray)
                         tb.BorderThickness = New Thickness(0)
                         sp.Children.Add(tb)
                         TVI.Header = sp
                     End Sub)
            AddTVI(TVI, Watcher, Value)
        Else
            Dim Header As String = Value.Name & vbCrLf & "    " & ValueToString
            Dim TVI As TreeViewItem = Await CreateTreeItem(Value.Type, Header, Value.Guid, Nothing)
            AddTVI(TVI, Watcher, Value)
            If Not Value.Flags.isChild Then PrepareInspectableNode(TVI, Value)
        End If
    End Sub

    Private Sub AddTVI(TVI As TreeViewItem, Watcher As DebugWatcher, Value As DebugValue)
        If Not ContainsTVI(Value) Then
            InvokeUI(Sub()
                         Try
                             Dim ParentTVI As TreeViewItem
                             If Watcher.isChild Then
                                 ParentTVI = TreeItems(Watcher.ParentValueGuid)
                             Else
                                 ParentTVI = TreeItems(Watcher.Guid)
                             End If

                             ParentTVI.Items.Add(TVI)
                             TreeItems.Add(Value.Guid, TVI)
                             ValueChangedAnim(ParentTVI)
                             ValueChangedAnim(TVI)
                         Catch : End Try
                     End Sub)
        End If
    End Sub

    Private Function ValueToString(Value As Object)
        Return Value.ToString()
    End Function

End Class
