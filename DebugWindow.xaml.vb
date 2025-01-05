Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Reflection
Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Controls.Primitives
Imports System.Windows.Forms.Integration
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Xml.Serialization
Imports ControlzEx.Theming
Imports JackDebug.WPF.Collections
Imports JackDebug.WPF.Values
Imports MahApps.Metro.Controls
Imports MicroSerializationLibrary
Imports MicroSerializationLibrary.Serialization
Imports Windows.Media.Capture

Public Class DebugWindow
    Inherits MetroWindow

#Region "UI"

    Private isUserChangingLower As Boolean = False,
            isUserChangingUpper As Boolean = False,
            Range As Integer

    Public Sub New()

        ' This call is required by the designer.
        InitializeComponent()

        ' Add any initialization after the InitializeComponent() call.

        ThemeManager.Current.ChangeTheme(Me, "Dark.Red")
        SetRange()
    End Sub

    Private Sub SetRange()
        If isUserChangingLower Or isUserChangingUpper Then Return
        Range = If(isAtBeginning And isAtEnd, MaxRange, Math.Min(Upper - Lower, MaxRange))
    End Sub

    Private Sub DebugWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight
        BackgroundWorker.RunWorkerAsync()
        Enabled = False
    End Sub

    Private Sub DebugWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        Enabled = False
        BackgroundWorker.CancelAsync()
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
        Enabled = Not Enabled
    End Sub

    Private Sub Slider_LowerValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles Slider.LowerValueChanged
        If IsMaxWidth Then
            e.Handled = True
            Range = MaxRange
            Lower = Upper - MaxRange
        End If
        _Lower = Slider.LowerValue
    End Sub

    Private Sub Slider_UpperValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles Slider.UpperValueChanged
        If IsMaxWidth Then
            e.Handled = True
            Lower = Upper - Range
        End If
        _Upper = Slider.UpperValue
    End Sub

    Private Sub Slider_LowerThumbDragStarted(sender As Object, e As DragStartedEventArgs) Handles Slider.LowerThumbDragStarted
        SetRange()
        isUserChangingLower = True
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

#End Region

#Region "Caches"

    Private ts As TimeSpan = TimeSpan.FromTicks(1000)
    Private WithEvents CurrentTimeline As ValueTimeline
    Private CurrentWatcher As DebugWatcher

    Private CurrentWatchers As New Dictionary(Of String, DebugWatcher)

#End Region

#Region "Drawing"

    Public Sub ClearPoints()
        Application.Current.Dispatcher.Invoke(Sub() Points.Clear())
        DrawIndex = 0
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight
    End Sub

    Public Sub DrawTimeline(cw As String)
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
            Application.Current.Dispatcher.Invoke(
            Sub()
                ClearPoints()
                Dim l As Integer = splice.Values.Length - 1
                Dim pointWidth As Double = PlotWidth / l

                Dim isBoolean As Boolean = splice.Values.First().Flags.isBoolean

                CreatePoint(DrawIndex, 0)
                CreatePoint(DrawIndex, PlotHeight)
                CreatePoint(DrawIndex, 0)
                For i As Integer = 0 To splice.Values.Length - 1
                    Dim v As DebugValue = splice.Values(i)
                    Dim InterpolatedValue As Double

                    If isBoolean Then
                        If v.Value Then
                            InterpolatedValue = PlotHeight
                        Else
                            InterpolatedValue = 0
                        End If
                    Else
                        InterpolatedValue = Interpolate(v.Value, CurrentTimeline.LowestValue.Value, CurrentTimeline.HighestValue.Value, 0, PlotHeight)
                    End If

                    CreatePoint(DrawIndex, InterpolatedValue)
                    CreatePoint(DrawIndex + pointWidth, InterpolatedValue)

                    DrawIndex += pointWidth
                Next

                CreatePoint(DrawIndex, 0)
                If isBoolean Then
                    LowLabel.Text = "False"
                    HighLabel.Text = "True"
                Else
                    LowLabel.Text = CurrentTimeline.LowestValue.Value
                    HighLabel.Text = CurrentTimeline.HighestValue.Value
                End If
            End Sub)
        End If

    End Sub

    Public Sub CreatePoint(x As Double, y As Double)
        Application.Current.Dispatcher.Invoke(Sub() Points.Add(New Point(x, PlotHeight - y)))
    End Sub

#End Region

#Region "Shared Properties"
    Public Shared Property MaxRange As Integer = 1000
    Public Shared Property AnimationDuration As TimeSpan = TimeSpan.FromSeconds(0.75)
    Public Shared Property DefaultBackground As Color = Color.FromArgb(255, 37, 37, 37)
    Public Shared Property ValueChangedAnimation As New ColorAnimation(Colors.LimeGreen, DefaultBackground, AnimationDuration)

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
            _Enabled = value
            For i As Integer = 0 To DebugWatcher.Watchers.Count - 1
                Dim w As DebugWatcher = DebugWatcher.Watchers(i)
                w.isEnabled = value
            Next
            If value Then BackgroundWorker.RunWorkerAsync() Else BackgroundWorker.CancelAsync()
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
            Return DebugWatcher.Watchers.Count
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

    Private WatcherItems As New Dictionary(Of String, TreeViewItem)
    Private TreeItems As New Dictionary(Of String, TreeViewItem)

    Private Function CreateTreeItem(Type As Type, Header As String) As TreeViewItem
        Dim NewValue As TreeViewItem = Nothing
        Application.Current.Dispatcher.Invoke(
            Sub()
                NewValue = New TreeViewItem
                With NewValue
                    .ToolTip = Type.Name
                    .Header = Header
                    .Background = New SolidColorBrush(DefaultBackground)
                End With
            End Sub)

        Return NewValue
    End Function

#End Region

#Region "Animations"

    Public Sub ValueChangedAnim(TreeViewItem As TreeViewItem, Indexies As List(Of Integer))
        Application.Current.Dispatcher.Invoke(
            Sub()
                If Indexies IsNot Nothing AndAlso Indexies.Count > 0 Then
                    For i As Integer = 0 To Indexies.Count - 1
                        If TreeViewItem.Items.Count - 1 >= i Then
                            Dim aTVI As TreeViewItem = TreeViewItem.Items(i)
                            aTVI.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation)
                        End If
                    Next
                End If
                TreeViewItem.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation)
            End Sub)
    End Sub

#End Region

#Region "Functions"

    Private Sub SetTimeline(cw As DebugWatcher, GUID As String)
        ClearPoints()
        CurrentWatcher = cw
        CurrentTimeline = CurrentWatcher.Timelines(GUID)
    End Sub

    Private Function SubItemSelected(TVI As TreeViewItem) As Boolean
        For i As Integer = 0 To TVI.Items.Count - 1
            Dim item As TreeViewItem = TVI.Items(i)
            If item.IsSelected Then
                Return True
            End If
        Next
        Return False
    End Function

#End Region

#Region "Background Worker"

    Private WithEvents BackgroundWorker As New BackgroundWorker With {.WorkerSupportsCancellation = True}
    ''' <summary>
    ''' Tree View Item Updates
    ''' </summary>
    Private Sub BackgroundWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles BackgroundWorker.DoWork
        Do While Enabled
            Dim nCount As Integer = WatcherCount
            If nCount <> _WatcherCount Then
                For i As Integer = 0 To DebugWatcher.Watchers.Count - 1
                    If Not Enabled Then Exit For
                    Dim w As DebugWatcher = DebugWatcher.Watchers(i)
                    Dim t As Type = w.AttachedObject.GetType
                    If Not CurrentWatchers.ContainsKey(w.GUID) AndAlso Not IsImmutable(t) Then
                        CurrentWatchers.Add(w.GUID, w)
                        Dim NewWatcher As TreeViewItem = CreateTreeItem(t, w.Name)
                        Application.Current.Dispatcher.Invoke(
                            Sub()
                                If Not TreeItems.ContainsKey(w.GUID) Then
                                    Watchers.Items.Add(NewWatcher)
                                    WatcherItems.Add(w.GUID, NewWatcher)

                                    AddHandler w.OnValueCalculated, AddressOf ValueCalculated
                                End If
                            End Sub)
                    End If
                Next
                _WatcherCount = nCount
            End If
            If NotNothing(CurrentWatcher) Then
                DrawTimeline(CurrentWatcher.GUID)
            End If
            Thread.Sleep(1)
        Loop
    End Sub

#End Region

    ''' <summary>
    ''' Fires when the watcher has a new value.
    ''' </summary>
    ''' <param name="Watcher">Current Watcher</param>
    ''' <param name="Value">Current Value</param>
    Private Sub ValueCalculated(Watcher As DebugWatcher, Value As DebugValue)
        If Value Is Nothing Then Return

        Dim TimelineDiff As Integer = 0

        If TreeItems.ContainsKey(Value.GUID) Then
            If CurrentTimeline IsNot Nothing AndAlso Value.GUID = CurrentTimeline.GUID Then TimelineDiff += 1

            Dim TVI As TreeViewItem = TreeItems(Value.GUID)
            If Value.Flags.isArray Or Value.Flags.isList Or Value.Flags.isDictionary Then
                ''' TODO: 
                ''' Split into seperate methods and/or functions
                ''' Optimize Thread Switching
                If Value.Flags.isDictionary AndAlso Value.KeyList IsNot Nothing Then
                    For i As Integer = i To Value.KeyList.Count - 1
                        Dim Index As Integer = i
                        Dim Header As String = "[" & Index & "]" & Value.KeyList(Index) & ": " & Value.Type.Name
                        If Index > TVI.Items.Count - 1 Then
                            Dim aTVI As TreeViewItem = CreateTreeItem(Value.ValueList(Index).GetType, Header)
                            AddHandler aTVI.Selected, Sub() SetTimeline(Watcher, Value.GUID)
                            Application.Current.Dispatcher.Invoke(Sub() TVI.Items.Add(aTVI))
                        Else
                            Application.Current.Dispatcher.Invoke(Sub() TVI.Items(Index).Header = Header)
                        End If
                    Next
                ElseIf Value.Flags.isList AndAlso Value.Value IsNot Nothing Then
                    For i As Integer = i To Value.Length - 1
                        Dim Header As String = "[" & i & "] " & Value.Value(i).GetType.Name
                        Dim Index As Integer = i
                        If Index > TVI.Items.Count - 1 Then
                            Dim aTVI As TreeViewItem = CreateTreeItem(Value.Value(Index).GetType, Header)
                            AddHandler aTVI.Selected, Sub() SetTimeline(Watcher, Value.GUID)
                            Application.Current.Dispatcher.Invoke(Sub() TVI.Items.Add(aTVI))
                        Else
                            Application.Current.Dispatcher.Invoke(Sub() TVI.Items(Index).Header = Header)
                        End If
                    Next
                ElseIf Value.Flags.isArray AndAlso Value.Value IsNot Nothing Then
                    Dim ArrayType As String = ""
                    For i As Integer = 0 To Value.Length - 1
                        Dim Index As Integer = i
                        If Index < 0 Then Exit For
                        If Value.Length - 1 < Index Then Exit For
                        Try
                            Dim ArrayItem As Object = Value.Value(Index)
                            If ArrayType = "" Then ArrayType = ArrayItem.GetType.Name
                            Dim Header As String = "[" & Index & "]: " & ArrayType
                            Dim ItemCount1 As Integer = 0

                            Application.Current.Dispatcher.Invoke(Sub() ItemCount1 = TVI.Items.Count - 1)
                            If Index > ItemCount1 Then
                                Dim aTVI As TreeViewItem = CreateTreeItem(ArrayItem.GetType, Header)
                                AddHandler aTVI.Selected, Sub() SetTimeline(Watcher, Value.GUID)
                                Application.Current.Dispatcher.Invoke(Sub()
                                                                          Try
                                                                              TVI.Items.Add(aTVI)
                                                                          Catch ex As Exception
                                                                              '''TODO: Not sure why this thows an out of bounds exception..
                                                                          End Try
                                                                      End Sub)
                            Else
                                Application.Current.Dispatcher.Invoke(Sub()
                                                                          Try
                                                                              TVI.Items(Index).Header = Header
                                                                          Catch ex As Exception
                                                                              '''TODO: Check for out of bounds if nessesary. Haven't seen it happen, yet..
                                                                          End Try
                                                                      End Sub)
                            End If
                        Catch
                        End Try
                    Next
                End If
                Dim ItemCount2 As Integer = 0
                Application.Current.Dispatcher.Invoke(Sub() ItemCount2 = TVI.Items.Count - 1)
                If ItemCount2 > Value.Length - 1 Then
                    For x As Integer = ItemCount2 To Value.Length - 1 Step -1
                        Dim Index As Integer = x
                        If TVI.Items.Count > 0 Then
                            Application.Current.Dispatcher.Invoke(Sub() TVI.Items.RemoveAt(Index))
                        Else
                            Exit For
                        End If
                    Next
                End If
            Else
                TimelineDiff += IterateValues(Watcher, TVI, Value, Value)
                ''' TODO: 
                ''' Show Values!!!!
                'TVI.Header = v.Value.Tostring()
            End If
        Else
            Dim TVI As TreeViewItem = CreateTreeItem(Value.Type, Value.Name)
            AddHandler TVI.Selected, Sub()
                                         If Not SubItemSelected(TVI) Then
                                             SetTimeline(Watcher, Value.GUID)
                                         End If
                                     End Sub



            If Value.Flags.isArray Or Value.Flags.isList Or Value.Flags.isDictionary Then
                For i As Integer = 0 To Value.Length - 1
                    Dim ArrayItem As Object = Value.Value(i)
                    Dim aTVI As TreeViewItem = CreateTreeItem(ArrayItem.GetType, ArrayItem.GetType().Name)
                    AddHandler aTVI.Selected, Sub() SetTimeline(Watcher, Value.GUID)
                    Application.Current.Dispatcher.Invoke(Sub() TVI.Items.Add(aTVI))
                Next
            End If
            If TreeItems.ContainsKey(Value.GUID) Then
                TVI = Nothing
                _TimelineDifference = TimelineDiff
                Return
            End If

            TreeItems.Add(Value.GUID, TVI)
            AddHandler Watcher.Timelines(Value.GUID).ValueChanged, Sub(Timeline As ValueTimeline, ChangedValue As DebugValue, ArrayIndexies As List(Of Integer)) ValueChangedAnim(TVI, ArrayIndexies)
            Application.Current.Dispatcher.Invoke(
                Sub() WatcherItems(Watcher.GUID).Items.Add(TVI))
        End If

        _TimelineDifference = TimelineDiff
    End Sub

    ''' <summary>
    ''' Iterate a Fields Child Values & Update UI
    ''' </summary>
    ''' <param name="cw"></param>
    ''' <param name="TVI"></param>
    ''' <param name="CurrentValue"></param>
    ''' <param name="Parent"></param>
    ''' <returns></returns>
    Private Function IterateValues(cw As DebugWatcher, TVI As TreeViewItem, CurrentValue As DebugValue, Parent As DebugValue) As Integer
        If CurrentValue Is Nothing Then Return 0
        If CurrentValue.Children Is Nothing Then Return 0
        If CurrentValue.Children.Length > 0 Then
            Dim TimelineDiff As Integer = 0

            For i As Integer = 0 To CurrentValue.Children.Length - 1
                Dim ArrayItem As DebugValue = CurrentValue.Children.Values(i)

                If ArrayItem Is Nothing Then Continue For
                If Not ArrayItem.ValueChanged Then Continue For

                If ArrayItem.Value Is Parent.Value Then
                    Continue For
                End If

                If ArrayItem.GUID = CurrentTimeline.GUID Then TimelineDiff += 1

                If i > TVI.Items.Count - 1 Then
                    Dim aTVI As TreeViewItem = Nothing
                    Application.Current.Dispatcher.Invoke(Sub()
                                                              aTVI = CreateTreeItem(ArrayItem.GetType, ArrayItem.Name)
                                                              AddHandler aTVI.Selected, Sub() SetTimeline(cw, CurrentValue.GUID)
                                                              TVI.Items.Add(aTVI)
                                                          End Sub)
                    TimelineDiff += IterateValues(cw, aTVI, ArrayItem, Parent)
                Else
                    Dim aTVI As TreeViewItem = Nothing
                    Dim index As Integer = i
                    Application.Current.Dispatcher.Invoke(Sub() aTVI = TVI.Items(index))
                    TimelineDiff += IterateValues(cw, aTVI, ArrayItem, Parent)
                End If
            Next

            Return TimelineDiff
        End If
        Return 0
    End Function

End Class
