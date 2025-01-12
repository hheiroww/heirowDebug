Imports System.Collections.ObjectModel
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
Imports System.Xml.Serialization
Imports ControlzEx.Theming
Imports JackDebug.WPF.Collections
Imports JackDebug.WPF.Values
Imports MahApps.Metro.Controls
Imports MicroSerializationLibrary
Imports MicroSerializationLibrary.Serialization

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
        Enabled = True
    End Sub

    Private Sub Watchers_SelectedItemChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Object)) Handles Watchers.SelectedItemChanged
        Dim TreeItem As TreeViewItem = DirectCast(e.NewValue, TreeViewItem)
        If Not SubItemSelected(TreeItem) Then SetTimeline(TreeItem.Tag)
    End Sub

    Private Sub DebugWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        Enabled = False
        BackgroundWorker.Dispose()
        For i As Integer = 0 To DebugWatcher.Watchers.Count - 1
            DebugWatcher.Watchers.Values(i).Dispose()
        Next
        DebugWatcher.Watchers.Clear()
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
    Public ConcurrentIterations As New List(Of String)
    Private InitializedWatchers As New Dictionary(Of String, DebugWatcher)

#End Region

#Region "Drawing"

    Public Sub ClearPoints()
        InvokeUI(Sub() Points.Clear())
        DrawIndex = 0
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight
    End Sub

    Public Sub DrawTimeline()
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
            If CurrentTimeline.HighestValue Is Nothing Then Return
            If CurrentTimeline.LowestValue Is Nothing Then Return
            InvokeUI(
                Sub()
                    ClearPoints()
                    Dim l As Integer = splice.Values.Length - 1
                    Dim pointWidth As Double = PlotWidth / l

                    Dim Flags As TypeFlags = splice.Values.Last().Flags

                    CreatePoint(DrawIndex, 0)
                    CreatePoint(DrawIndex, PlotHeight)
                    CreatePoint(DrawIndex, 0)
                    For i As Integer = 0 To splice.Values.Length - 1
                        Dim v As DebugValue = splice.Values(i)
                        Dim InterpolatedValue As Double

                        If Flags.isBoolean Then
                            If v.Value Then
                                InterpolatedValue = PlotHeight
                            Else
                                InterpolatedValue = 0
                            End If
                        ElseIf Flags.isDrawingRectangle Then
                        ElseIf Flags.isShapesRect Then
                        ElseIf Flags.isDrawingPoint Then
                        ElseIf Flags.isWindowsPoint Then
                        ElseIf Flags.isNumeric Then
                            InterpolatedValue = Interpolate(v.Value, CurrentTimeline.LowestValue, CurrentTimeline.HighestValue, 0, PlotHeight)
                        End If

                        CreatePoint(DrawIndex, InterpolatedValue)
                        CreatePoint(DrawIndex + pointWidth, InterpolatedValue)

                        DrawIndex += pointWidth
                    Next

                    CreatePoint(DrawIndex, 0)

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
                Dim w As DebugWatcher = DebugWatcher.Watchers.Values(i)
                w.isEnabled = value
            Next
            If value Then
                ForceInvoke(Sub()
                                Do While BackgroundWorker IsNot Nothing
                                    Thread.Sleep(1)
                                Loop
                            End Sub)
                BackgroundWorker = New Timer(New TimerCallback(AddressOf BackgroundWorker_DoWork), Nothing, 0, Timeout.Infinite)
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

    Private TreeItems As New Dictionary(Of String, TreeViewItem)

    Private Function CreateTreeItem(Type As Type, Header As String, TreeItemGuid As String, Optional ToolTip As String = Nothing) As TreeViewItem
        Dim NewValue As TreeViewItem = Nothing

        InvokeUI(Sub()
                     NewValue = New TreeViewItem
                     NewValue.Tag = TreeItemGuid
                     If ToolTip = Nothing Then ToolTip = Type.Name
                     With NewValue
                         .ToolTip = ToolTip
                         .Header = Header
                         .Background = New SolidColorBrush(DefaultBackground)
                     End With
                 End Sub)

        Return NewValue
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
        If ValueTimeline.Timelines.ContainsKey(Guid) Then
            CurrentTimeline = ValueTimeline.Timelines(Guid)
        End If
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

    Private WithEvents BackgroundWorker As Timer
    ''' <summary>
    ''' Tree View Item Updates
    ''' </summary>
    Private Sub BackgroundWorker_DoWork(state As Object)
        Dim ResultInterval As TimeSpan
        Dim ms As Double = 0

        Do While Enabled
            Dim nCount As Integer = WatcherCount
            If nCount <> InitializedWatchers.Count Then
                For i As Integer = 0 To DebugWatcher.Watchers.Count - 1
                    If Not Enabled Then Exit For

                    Dim w As DebugWatcher = DebugWatcher.Watchers.Values.ElementAt(i)
                    Dim t As Type = w.AttachedObject.GetType

                    Dim WatcherInitialized As Boolean = InitializedWatchers.ContainsKey(w.Guid)
                    If Not WatcherInitialized Then

                        If w.isChild Then
                            Dim ParentInitialized As Boolean = InitializedWatchers.ContainsKey(w.Parent.Guid)
                            Dim ParentValueInitialized As Boolean = TreeItems.ContainsKey(w.ParentValueGuid)
                            If ParentInitialized And ParentValueInitialized Then
                                InvokeUI(Sub()
                                             InitializedWatchers.Add(w.Guid, w)
                                             ''' Recursive functionality disabled until finished.
                                             '''    AddHandler w.ValueCalculated, AddressOf ValueCalculated
                                             '''    AddHandler w.ValueChanged, AddressOf ValueChanged
                                         End Sub)
                            End If
                        Else
                            Dim NewWatcher As TreeViewItem = CreateTreeItem(t, w.Name, w.Guid)
                            InvokeUI(Sub()
                                         InitializedWatchers.Add(w.Guid, w)
                                         Watchers.Items.Add(NewWatcher)
                                         TreeItems.Add(w.Guid, NewWatcher)
                                         AddHandler w.ValueCalculated, AddressOf ValueCalculated
                                         AddHandler w.ValueChanged, AddressOf ValueChanged
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
            If ms < IntervalMilliseconds Then
                Dim diff As Double = Math.Min(Math.Max(0, Interval.TotalMilliseconds - ResultInterval.TotalMilliseconds), Interval.TotalMilliseconds)
                Dim ActualWaitTime As TimeSpan = TimeSpan.FromMilliseconds(diff)
                If diff > 0 Then Thread.Sleep(ActualWaitTime)
            Else
                Thread.Sleep(MinimumInterval)
            End If
        Loop
        BackgroundWorker = Nothing
    End Sub

#End Region

    Private Sub ValueChanged(Watcher As DebugWatcher, Value As DebugValue, ArrayIndexies As List(Of Integer))
        InvokeUI(Sub()
                     If ContainsTVI(Value) Then
                         Dim TVI As TreeViewItem = TreeItems(Value.Guid)
                         If Value.Flags.isCollection Then
                             UpdateCollectionTVI(Watcher, Value)
                         Else
                             UpdateSingleTVI(Watcher, Value)
                         End If
                     End If
                 End Sub)
        IterateIndex(Value)
    End Sub

    Private Sub ValueCalculated(Watcher As DebugWatcher, Value As DebugValue)
        If Value Is Nothing Then Return
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

    Private Sub UpdateArrayTVI(TVI As TreeViewItem, Value As DebugValue)
        Dim ArrayType As Type = Nothing
        For i As Integer = 0 To Value.Length - 1
            If Value.Length - 1 < i Then Exit For
            Dim Index As Integer = i
            If Index < 0 Then Exit For
            Try
                Dim ArrayItem As Object = Value.Value(Index)
                If ArrayType Is Nothing Then ArrayType = ArrayItem.GetType()
                Dim ValueToString As String = If(IsNothing(ArrayItem), "NULL", GetValueString(ArrayItem.ToString()))
                Dim Header As String = "[" & Index & "] " & ValueToString
                Dim ItemCount1 As Integer = ReturnValueUI(Function() TVI.Items.Count - 1)
                If Index > ItemCount1 Then
                    Dim aTVI As TreeViewItem = CreateTreeItem(ArrayType, Header, Value.Guid)
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
            Catch
            End Try
        Next
    End Sub

    Private Sub UpdateListTVI(TVI As TreeViewItem, Value As DebugValue)
        Dim ArrayType As Type = Nothing
        Dim L As Integer = Value.Length - 1
        For i As Integer = i To L
            If i > Value.Value.Count - 1 Then Exit For
            Dim Index As Integer = i
            Dim ArrayItem As Object = Value.Value(Index)
            Dim ValueToString As String = If(IsNothing(ArrayItem), "NULL", GetValueString(ArrayItem.ToString()))
            If ArrayType Is Nothing Then ArrayType = ArrayItem.GetType()
            Dim Header As String = "[" & Index & "] " & ArrayType.Name & ": " & ValueToString
            If Index > TVI.Items.Count - 1 Then
                Dim aTVI As TreeViewItem = CreateTreeItem(ArrayType, Header, Value.Guid)
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
        Next
    End Sub

    Private Sub UpdateDictionaryTVI(TVI As TreeViewItem, Value As DebugValue)
        Dim ArrayType As Type = Nothing
        For i As Integer = i To Value.KeyList.Count - 1
            Dim Index As Integer = i
            If Index > Value.Value.Length - 1 Then Exit For
            Dim ArrayItemKey As Object = Value.KeyList(Index)
            Dim ArrayItemValue As Object = Value.ValueList(Index)
            Dim ValueToString As String = If(IsNothing(ArrayItemValue), "NULL", GetValueString(ArrayItemValue.ToString()))
            If ArrayType Is Nothing Then ArrayType = ArrayItemValue.GetType()
            Dim Header As String = "[" & Index & "]" & ArrayItemKey.ToString() & ": " & ValueToString
            If Index > TVI.Items.Count - 1 Then
                Dim aTVI As TreeViewItem = CreateTreeItem(ArrayType, Header, Value.Guid)
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
        Next
    End Sub

    Private Sub PruneCollection(TVI As TreeViewItem, Value As DebugValue)
        If Value.Length <= 0 AndAlso Not Value.Flags.isChild Then TVI.Items.Clear()
        Dim ItemCount2 As Integer = ReturnValueUI(Function() TVI.Items.Count - 1)
        If ItemCount2 > Value.Length - 1 Then
            Dim Length As Integer = 0
            For x As Integer = ItemCount2 To Length Step 1
                Dim Index As Integer = x
                Dim [Continue] As Boolean = False
                InvokeUI(Sub()
                             If TVI.Items.Count > 0 AndAlso TVI.Items.Count - 1 <= Index Then
                                 Try : TVI.Items.RemoveAt(Index) : Catch : End Try
                             Else
                                 [Continue] = True
                             End If
                         End Sub)
                If [Continue] Then Continue For
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
        Dim ValueToString As String = If(IsNothing(Value.Value), "NULL", GetValueString(Value.Value.ToString()))
        Dim Header As String = Value.Name & vbCrLf & "    " & ValueToString
        InvokeUI(Sub() UpdateHeader(GetTVI(Value), Header))
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

    Private Sub CreateCollectionTVI(Watcher As DebugWatcher, Value As DebugValue)
        Dim TVI As TreeViewItem = CreateTreeItem(Value.Type, Value.Name, Value.Guid)

        Dim ArrayType As Type = Nothing
        Dim index As Integer = 0
        Dim L As Integer = Value.Length - 1
        For i As Integer = 0 To L
            index = i
            If Value.Flags.isDictionary Then
                If index > Value.Value.Count - 1 Then Exit For
                Value.Value.GetType().GetGenericArguments()
                ArrayType = Value.Type.GetGenericArguments()(1)
            ElseIf Value.Flags.isList Then
                If index > Value.Value.Count - 1 Then Exit For
                ArrayType = Value.Type.GetGenericArguments()(0)
            ElseIf Value.Flags.isArray Then
                If index > Value.Value.Length - 1 Then Exit For
                ArrayType = Value.Type.GetElementType()
            End If

            Dim ArrayItem As Object = Value.Value(index)
            Dim aTVI As TreeViewItem = CreateTreeItem(ArrayType, ArrayType.Name, Value.Guid)

            InvokeUI(Sub()
                         If Value.Flags.isDictionary Then
                             If index > Value.Value.Count - 1 Then Return
                         ElseIf Value.Flags.isList Then
                             If index > Value.Value.Count - 1 Then Return
                         ElseIf Value.Flags.isArray Then
                             If index > Value.Value.Length - 1 Then Return
                         End If
                         Try : TVI.Items.Add(aTVI) : Catch : End Try
                     End Sub)

        Next
        If Watcher.isChild Then
            AddChildTVI(TVI, Watcher, Value)
        Else
            AddTVI(TVI, Watcher, Value)
        End If
    End Sub

    Private Sub AddChildTVI(TVI As TreeViewItem, Watcher As DebugWatcher, Value As DebugValue)
        If Not ContainsTVI(Watcher.ParentValueGuid) Then
            InvokeUI(Sub()
                         Try
                             Dim p As TreeViewItem = TreeItems(Watcher.ParentValueGuid)
                             p.Items.Add(TVI)
                             TreeItems.Add(Value.Guid, TVI)
                             ValueChangedAnim(p)
                             ValueChangedAnim(TVI)
                         Catch : End Try
                     End Sub)
        End If
    End Sub

    Private Sub AddTVI(TVI As TreeViewItem, Watcher As DebugWatcher, Value As DebugValue)
        If Not ContainsTVI(Value) Then
            InvokeUI(Sub()
                         Try
                             Dim p As TreeViewItem = TreeItems(Watcher.Guid)
                             p.Items.Add(TVI)
                             TreeItems.Add(Value.Guid, TVI)
                             ValueChangedAnim(p)
                             ValueChangedAnim(TVI)
                         Catch : End Try
                     End Sub)
        End If
    End Sub

    Private Sub CreateSingleTVI(Watcher As DebugWatcher, Value As DebugValue)
        Dim ValueToString As String = If(IsNothing(Value.Value), "NULL", GetValueString(Value.Value))
        Dim Header As String = Value.Name & vbCrLf & "    " & ValueToString
        Dim TVI As TreeViewItem = CreateTreeItem(Value.Type, Header, Value.Guid)
        If Watcher.isChild Then
            AddChildTVI(TVI, Watcher, Value)
        Else
            AddTVI(TVI, Watcher, Value)
        End If
    End Sub

    Private Function GetValueString(Value As Object)
        Return Value.ToString() '.Replace(",", vbCrLf & "         ")
    End Function
End Class
