Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Reflection
Imports System.Security.Cryptography.Pkcs
Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Windows.Shapes
Imports System.Windows.Threading
Imports System.Xml.Serialization
Imports ControlzEx.Theming
Imports LiveCharts.Events
Imports MahApps.Metro.Controls
Imports MicroSerializationLibrary
Imports MicroSerializationLibrary.Serialization

Public Class DebugWindow
    Inherits MetroWindow

#Region "UI"

    Private Sub DebugWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight
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
        For i As Integer = 0 To CurrentWatchers.Count - 1
            Dim w As DebugWatcher = CurrentWatchers(i)
            w.isEnabled = ToggleEnabled.IsOn
        Next
    End Sub

    Private Sub Slider_LowerValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles Slider.LowerValueChanged
        If IsMaxWidth Then
            e.Handled = True
            Lower = Upper - MaxValues
        End If
        _Lower = Slider.LowerValue
    End Sub

    Private Sub Slider_UpperValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double)) Handles Slider.UpperValueChanged
        If IsMaxWidth Then
            e.Handled = True
            Lower = Upper - MaxValues
        End If
        _Upper = Slider.UpperValue
    End Sub

#End Region

#Region "Caches"

    Private ts As TimeSpan = TimeSpan.FromTicks(10000)
    Private CurrentTimeline As Object
    Private SelectedWatcher As Guid

    Private TimelineKeys As New Dictionary(Of Guid, Integer)
    Private CurrentWatchers As New List(Of DebugWatcher)
    Private FieldTimelines As New List(Of Dictionary(Of FieldReference, DebugValueTimeline))
    Private PropertyTimelines As New List(Of Dictionary(Of PropertyReference, DebugValueTimeline))

#End Region

#Region "Drawing"

    Public Sub ClearPoints()
        Application.Current.Dispatcher.Invoke(Sub() Points.Clear())
        DrawIndex = 0
        _PlotWidth = Plot.ActualWidth
        _PlotHeight = Plot.ActualHeight
    End Sub

    Public Sub DrawTimeline(cw As Guid)
        If CurrentTimeline Is Nothing Then Return

        Dim ctl As DebugValueTimeline = If(CurrentTimeline.GetType Is GetType(PropertyReference), PropertyTimeline(cw, CurrentTimeline), FieldTimeline(cw, CurrentTimeline))

        If ctl Is Nothing Then Return

        Dim isAtEnd As Boolean = Upper = Maximum
        Dim isAtBeginning As Boolean = Lower = 0
        Maximum = ctl.Maximum - 1

        If isAtEnd Then
            Upper = Maximum
        End If

        If isAtBeginning Then
            If Not IsMaxWidth Then
                Lower = 0
            Else
                Lower = Upper - MaxValues
            End If
        ElseIf isAtEnd Then
            If IsMaxWidth Then
                Lower = Upper - MaxValues
            Else
                Lower += TimelineDifference
            End If
        End If

        If DrawIndex = Upper Then Return

        Dim splice As TimelineSplice = ctl.GetValuesWithin(Lower, Upper)
        Application.Current.Dispatcher.Invoke(
            Sub()
                ClearPoints()

                If splice.isGraphable AndAlso splice.Values.Count > 0 Then
                    Dim l As Integer = splice.Values.Length - 1
                    Dim pointWidth As Double = PlotWidth / l

                    Dim isBoolean As Boolean = splice.Values.First().Flags.isBoolean
                    CreatePoint(DrawIndex, 0)
                    If isBoolean Then CreatePoint(DrawIndex, PlotHeight)
                    If isBoolean Then CreatePoint(DrawIndex, 0)
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
                            If splice.LowestValue.Value = splice.HighestValue.Value Then
                                InterpolatedValue = PlotHeight
                            Else
                                InterpolatedValue = Interpolate(v.Value, splice.LowestValue.Value, splice.HighestValue.Value, 0, PlotHeight)
                            End If
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
                        LowLabel.Text = splice.LowestValue.Value
                        HighLabel.Text = splice.HighestValue.Value
                    End If

                Else
                    LowLabel.Text = ""
                    HighLabel.Text = ""
                End If

            End Sub)

    End Sub

    Public Sub CreatePoint(x As Double, y As Double)
        Application.Current.Dispatcher.Invoke(Sub() Points.Add(New Point(x, PlotHeight - y)))
    End Sub

#End Region

#Region "Properties"

    Public Property MaxValues As Integer = 1000

    Public Property Enabled As Boolean
        Get
            Return _Enabled
        End Get
        Set(value As Boolean)
            _Enabled = value
            If value Then
                BackgroundWorker.RunWorkerAsync()
            Else
                BackgroundWorker.CancelAsync()
            End If
        End Set
    End Property
    Private _Enabled As Boolean = True

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
            Return Upper - Lower > MaxValues
        End Get
    End Property

    Public ReadOnly Property FieldTimeline(cw As Guid, f As FieldReference) As DebugValueTimeline
        Get
            If FieldTimelines.Count > 0 Then
                If GetFieldTimeline(cw).ContainsKey(f) Then
                    Return GetFieldTimeline(cw)(f)
                Else
                    Return Nothing
                End If
            Else
                Return Nothing
            End If
        End Get
    End Property

    Public ReadOnly Property PropertyTimeline(cw As Guid, p As PropertyReference) As DebugValueTimeline
        Get
            If PropertyTimelines.Count > 0 Then
                If GetPropertyTimeline(cw).ContainsKey(p) Then
                    Return GetPropertyTimeline(cw)(p)
                Else
                    Return Nothing
                End If
            Else
                Return Nothing
            End If
        End Get
    End Property

    Private Property DrawIndex As Double = 0

    Private isAtEnd As Boolean = True

    Public Shared AnimationDuration As TimeSpan = TimeSpan.FromSeconds(0.75)

    Public Shared DefaultBackground As Color = Color.FromRgb(37, 37, 37)

    Public Shared ValueChangedAnimation As New ColorAnimation(Colors.LimeGreen, DefaultBackground, AnimationDuration)

#End Region

#Region "Tree View"

    Private TreeViewItems As New Dictionary(Of Guid, TreeViewItem)

    Private Function CreateTreeItem(GUID As Guid, Type As Type, Header As String) As TreeViewItem
        Dim NewValue As New TreeViewItem()
        With NewValue
            .Tag = GUID
            .ToolTip = Type.Name
            .Header = Header
            .Background = New SolidColorBrush(DefaultBackground)
        End With

        Return NewValue
    End Function

    Private WatcherItems As New Dictionary(Of Guid, WatcherDictionaryItem)

#End Region

#Region "Animations"

    Public Sub ValueChangedAnim(TreeViewItem As TreeViewItem)
        Application.Current.Dispatcher.Invoke(Sub() TreeViewItem.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation))
    End Sub

#End Region

#Region "Get/Set Functions"

    Private Function GetFieldTimeline(guid As Guid) As Dictionary(Of FieldReference, DebugValueTimeline)
        If Not TimelineKeys.ContainsKey(guid) Then Return Nothing
        Dim i As Integer = TimelineKeys(guid)
        Return FieldTimelines.ElementAt(i)
    End Function

    Private Function GetPropertyTimeline(guid As Guid) As Dictionary(Of PropertyReference, DebugValueTimeline)
        If Not TimelineKeys.ContainsKey(guid) Then Return Nothing
        Dim i As Integer = TimelineKeys(guid)
        Return PropertyTimelines.ElementAt(i)
    End Function

    Private Sub SetFieldTimeline(f As FieldReference)
        ClearPoints()
        CurrentTimeline = f
    End Sub

    Private Sub SetPropertyTimeline(p As PropertyReference)
        ClearPoints()
        CurrentTimeline = p
    End Sub

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
                    Dim w As DebugWatcher = DebugWatcher.Watchers(i)
                    Dim t As Type = w.AttachedObject.GetType
                    If Not CurrentWatchers.Contains(w) AndAlso Not IsImmutable(t) Then
                        CurrentWatchers.Add(w)
                        Application.Current.Dispatcher.Invoke(
                            Sub()
                                Dim NewWatcher As TreeViewItem = CreateTreeItem(w.GUID, t, w.Name)

                                If Not TreeViewItems.ContainsKey(w.GUID) Then
                                    Watchers.Items.Add(NewWatcher)
                                    TreeViewItems.Add(w.GUID, NewWatcher)

                                    Dim dItem As New WatcherDictionaryItem(NewWatcher)
                                    WatcherItems.Add(w.GUID, dItem)
                                    AddHandler w.FieldsCalculated, AddressOf Watcher_FieldsCalculated
                                    AddHandler w.PropertiesCalculated, AddressOf Watcher_PropertiesCalculated
                                    If Not TimelineKeys.ContainsKey(w.GUID) Then
                                        Dim Index As Integer = TimelineKeys.Count
                                        FieldTimelines.Add(New Dictionary(Of FieldReference, DebugValueTimeline))
                                        PropertyTimelines.Add(New Dictionary(Of PropertyReference, DebugValueTimeline))
                                        TimelineKeys.Add(w.GUID, Index)
                                    End If
                                End If

                            End Sub)
                    End If
                    If Not Enabled Then Exit For
                Next
                _WatcherCount = nCount
            End If

        Loop
    End Sub

#End Region

    Public Sub New()

        ' This call is required by the designer.
        InitializeComponent()

        ' Add any initialization after the InitializeComponent() call.
        BackgroundWorker.RunWorkerAsync()

        ThemeManager.Current.ChangeTheme(Me, "Dark.Red")
    End Sub

    ''' <summary>
    ''' Field Values -> Visual Data
    ''' </summary>
    ''' <param name="cw"></param>
    ''' <param name="Values"></param>
    Private Sub Watcher_FieldsCalculated(cw As DebugWatcher, Values As DebugValue())
        Dim ctl As Dictionary(Of FieldReference, DebugValueTimeline) = GetFieldTimeline(cw.GUID)
        If ctl Is Nothing Then Return

        Dim TimelineDiff As Integer = 0
        For Each v As DebugValue In Values
            If v Is Nothing Then Continue For
            Dim f As FieldReference = v.FieldReference
            Dim ValueItems As Dictionary(Of FieldReference, TreeViewItem) = WatcherItems(cw.GUID).Fields

            If ValueItems.ContainsKey(f) Then
                Dim lastVal
                lastVal = ctl(f).LastValue
                ctl(f).AddValue(v)

                If f Is CurrentTimeline Then TimelineDiff += 1
                If v.ValueChanged Then ctl(f).OnValueChangedEvent()

                Application.Current.Dispatcher.Invoke(
                    Sub()
                        Dim TVI As TreeViewItem = ValueItems(f)
                        If v.Flags.isArray Or v.Flags.isList Or v.Flags.isDictionary Then
                            If v.Flags.isDictionary AndAlso v.KeyList IsNot Nothing Then
                                For i As Integer = i To v.KeyList.Count - 1
                                    Dim Header As String = "[" & i & "]" & v.KeyList(i) & ": " & v.ValueList(i).Header
                                    If i > TVI.Items.Count - 1 Then
                                        Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, v.ValueList(i).GetType, Header)
                                        AddHandler aTVI.Selected, Sub() SetFieldTimeline(f)
                                        TVI.Items.Add(aTVI)
                                    Else
                                        TVI.Items(i).Header = Header
                                    End If
                                Next
                            ElseIf v.Flags.isList AndAlso v.Value IsNot Nothing Then
                                For i As Integer = i To v.Length - 1
                                    Dim Header As String = "[" & i & "] " & v.Value(i).GetType.Name
                                    If i > TVI.Items.Count - 1 Then
                                        Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, v.Value(i).GetType, Header)
                                        AddHandler aTVI.Selected, Sub() SetFieldTimeline(f)
                                        TVI.Items.Add(aTVI)
                                    Else
                                        TVI.Items(i).Header = Header
                                    End If
                                Next
                            ElseIf v.Flags.isArray AndAlso v.Value IsNot Nothing Then
                                For i As Integer = 0 To v.Length - 1
                                    If i < 0 Then Exit For
                                    If v.Length - 1 < i Then Exit For
                                    Try
                                        Dim ArrayItem As Object = v.Value(i)
                                        Dim Header As String = "[" & i & "]" & ArrayItem.GetType.Name
                                        If i > TVI.Items.Count - 1 Then
                                            Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, ArrayItem.GetType, Header)
                                            AddHandler aTVI.Selected, Sub() SetFieldTimeline(f)
                                            TVI.Items.Add(aTVI)
                                        Else
                                            TVI.Items(i).Header = Header
                                        End If
                                    Catch
                                    End Try
                                Next
                            End If
                            If TVI.Items.Count - 1 > v.Length - 1 Then
                                For x As Integer = TVI.Items.Count - 1 To v.Length - 1 Step -1
                                    If TVI.Items.Count > 0 Then
                                        TVI.Items.RemoveAt(x)
                                    Else
                                        Exit For
                                    End If
                                Next
                            End If
                        Else
                            TimelineDiff += IterateFieldSubValues(cw, ctl, TVI, v, v)
                            TVI.Header = v.Name
                        End If
                    End Sub)
            Else
                Dim TL As New DebugValueTimeline(f)
                ctl.Add(f, TL)
                Application.Current.Dispatcher.Invoke(
                Sub()
                    Dim TVI As TreeViewItem = CreateTreeItem(cw.GUID, v.Type, v.Name)

                    AddHandler TVI.Selected, Sub()
                                                 Dim SubItemSelected As Boolean = False
                                                 For i As Integer = 0 To TVI.Items.Count - 1
                                                     Dim item As TreeViewItem = TVI.Items(i)
                                                     If item.IsSelected Then
                                                         SubItemSelected = True
                                                         Exit For
                                                     End If
                                                 Next
                                                 If Not SubItemSelected Then SetFieldTimeline(f)
                                             End Sub
                    AddHandler TL.ValueChanged, Sub() Application.Current.Dispatcher.Invoke(Sub() TVI.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation))

                    If v.Flags.isArray Or v.Flags.isList Or v.Flags.isDictionary Then
                        For i As Integer = 0 To v.Length - 1
                            Dim ArrayItem As Object = v.Value(i)
                            Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, ArrayItem.GetType, ArrayItem.ToString())
                            AddHandler aTVI.Selected, Sub() SetFieldTimeline(f)
                            TVI.Items.Add(aTVI)
                        Next
                    End If

                    TL.AddValue(v)
                    ValueItems.Add(f, TVI)
                    WatcherItems(cw.GUID).TreeViewItem.Items.Add(TVI)
                End Sub)
            End If


        Next
        _TimelineDifference = TimelineDiff
        DrawTimeline(cw.GUID)
    End Sub

    ''' <summary>
    ''' Property Values -> Visual Data
    ''' </summary>
    ''' <param name="cw"></param>
    ''' <param name="Values"></param>
    Private Sub Watcher_PropertiesCalculated(cw As DebugWatcher, Values As DebugValue())
        Dim ctl As Dictionary(Of PropertyReference, DebugValueTimeline) = GetPropertyTimeline(cw.GUID)
        If ctl Is Nothing Then Return

        Dim TimelineDiff As Integer = 0
        For Each v As DebugValue In Values
            If v Is Nothing Then Continue For
            Dim p As PropertyReference = v.PropertyReference

            Dim ValueItems As Dictionary(Of PropertyReference, TreeViewItem) = WatcherItems(cw.GUID).Properties

            If ValueItems.ContainsKey(p) Then
                Dim lastVal
                lastVal = ctl(p).LastValue

                ctl(p).AddValue(v)

                If p Is CurrentTimeline Then TimelineDiff += 1
                If v.ValueChanged Then ctl(p).OnValueChangedEvent()

                Application.Current.Dispatcher.Invoke(
                    Sub()
                        Dim TVI As TreeViewItem = ValueItems(p)
                        If v.Flags.isArray Or v.Flags.isList Or v.Flags.isDictionary Then
                            If v.Flags.isDictionary AndAlso v.KeyList IsNot Nothing Then
                                For i As Integer = i To v.KeyList.Count - 1
                                    Dim Header As String = "[" & i & "]" & v.KeyList(i) & ": " & v.ValueList(i).GetType.Name
                                    If i > TVI.Items.Count - 1 Then
                                        Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, v.ValueList(i).GetType, Header)
                                        AddHandler aTVI.Selected, Sub() SetPropertyTimeline(p)
                                        TVI.Items.Add(aTVI)
                                    Else
                                        TVI.Items(i).Header = Header
                                    End If
                                Next
                            ElseIf v.Flags.isList AndAlso v.Value IsNot Nothing Then
                                For i As Integer = i To v.Length - 1
                                    Dim Header As String = "[" & i & "] " & v.Value(i).GetType.Name
                                    If i > TVI.Items.Count - 1 Then
                                        Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, v.Value(i).GetType, Header)
                                        AddHandler aTVI.Selected, Sub() SetPropertyTimeline(p)
                                        TVI.Items.Add(aTVI)
                                    Else
                                        TVI.Items(i).Header = Header
                                    End If
                                Next
                            ElseIf v.Flags.isArray AndAlso v.Value IsNot Nothing Then
                                For i As Integer = 0 To v.Length - 1
                                    If i < 0 Then Exit For
                                    If v.Length - 1 < i Then Exit For
                                    Try
                                        Dim ArrayItem As Object = v.Value(i)
                                        Dim Header As String = "[" & i & "]" & ArrayItem.GetType.Name
                                        If i > TVI.Items.Count - 1 Then
                                            Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, ArrayItem.GetType, Header)
                                            AddHandler aTVI.Selected, Sub() SetPropertyTimeline(p)
                                            TVI.Items.Add(aTVI)
                                        Else
                                            TVI.Items(i).Header = Header
                                        End If
                                    Catch
                                    End Try
                                Next
                            End If
                            If TVI.Items.Count - 1 > v.Length - 1 Then
                                For x As Integer = TVI.Items.Count - 1 To v.Length - 1 Step -1
                                    If TVI.Items.Count > 0 Then
                                        TVI.Items.RemoveAt(x)
                                    Else
                                        Exit For
                                    End If
                                Next
                            End If
                        Else
                            TimelineDiff += IteratePropertySubValues(cw, ctl, TVI, v, v)
                            TVI.Header = v.Name
                        End If
                    End Sub)
            Else
                Dim TL As New DebugValueTimeline(p)
                ctl.Add(p, TL)
                Application.Current.Dispatcher.Invoke(
                Sub()
                    Dim TVI As TreeViewItem = CreateTreeItem(cw.GUID, v.Type, v.Name)

                    AddHandler TVI.Selected, Sub()
                                                 Dim SubItemSelected As Boolean = False
                                                 For i As Integer = 0 To TVI.Items.Count - 1
                                                     Dim item As TreeViewItem = TVI.Items(i)
                                                     If item.IsSelected Then
                                                         SubItemSelected = True
                                                         Exit For
                                                     End If
                                                 Next
                                                 If Not SubItemSelected Then SetPropertyTimeline(p)
                                             End Sub
                    AddHandler TL.ValueChanged, Sub() Application.Current.Dispatcher.Invoke(Sub() TVI.Background.BeginAnimation(SolidColorBrush.ColorProperty, ValueChangedAnimation))

                    If v.Flags.isArray Then
                        Dim Dictionary As Boolean = IsDictionary(v.Value)
                        If Not Dictionary Then
                            For i As Integer = 0 To v.Length - 1
                                Dim ArrayItem As Object = v.Value(i)
                                Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, ArrayItem.GetType, ArrayItem.Header)
                                TVI.Items.Add(aTVI)
                            Next
                        End If
                    End If
                    TL.AddValue(v)
                    ValueItems.Add(p, TVI)
                    WatcherItems(cw.GUID).TreeViewItem.Items.Add(TVI)
                End Sub)
            End If


        Next
        _TimelineDifference = TimelineDiff
        Application.Current.Dispatcher.Invoke(Sub() DrawTimeline(cw.GUID))
    End Sub

    ''' <summary>
    ''' Iterate a Properties Child Values & Update UI
    ''' </summary>
    ''' <param name="cw"></param>
    ''' <param name="ctl"></param>
    ''' <param name="TVI"></param>
    ''' <param name="v"></param>
    ''' <param name="parent"></param>
    ''' <returns></returns>
    Private Function IteratePropertySubValues(cw As DebugWatcher, ctl As Dictionary(Of PropertyReference, DebugValueTimeline), TVI As TreeViewItem, v As DebugValue, parent As DebugValue) As Integer
        If v.SubValues.Length > 0 Then
            Dim TimelineDiff As Integer = 0
            For i As Integer = 0 To v.SubValues.Length - 1
                Dim ArrayItem As DebugValue = v.SubValues(i)

                If ArrayItem Is Nothing Then Continue For

                If ArrayItem.Value Is parent.Value Then
                    Continue For
                End If

                If ArrayItem IsNot Nothing AndAlso ArrayItem.PropertyReference Is CurrentTimeline Then TimelineDiff += 1

                If i > TVI.Items.Count - 1 Then
                    Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, ArrayItem.GetType, ArrayItem.Name)
                    TimelineDiff += IteratePropertySubValues(cw, ctl, aTVI, ArrayItem, parent)
                    If Not ctl.ContainsKey(ArrayItem.PropertyReference) Then
                        Dim TL As New DebugValueTimeline(ArrayItem.PropertyReference)
                        AddHandler TL.ValueChanged, Sub() ValueChangedAnim(aTVI)
                        ctl.Add(ArrayItem.PropertyReference, TL)
                    End If
                    AddHandler aTVI.Selected, Sub() SetPropertyTimeline(ArrayItem.PropertyReference)
                    TVI.Items.Add(aTVI)
                Else
                    Dim aTVI As TreeViewItem = TVI.Items(i)
                    aTVI.Header = ArrayItem.Name
                    If Not ctl.ContainsKey(ArrayItem.PropertyReference) Then
                        Dim TL As New DebugValueTimeline(ArrayItem.PropertyReference)
                        AddHandler TL.ValueChanged, Sub() ValueChangedAnim(aTVI)
                        ctl.Add(ArrayItem.PropertyReference, TL)
                        TL.AddValue(ArrayItem)
                    Else
                        ctl(ArrayItem.PropertyReference).AddValue(ArrayItem)
                    End If
                    TimelineDiff += IteratePropertySubValues(cw, ctl, aTVI, ArrayItem, parent)
                End If
                ctl(ArrayItem.PropertyReference).AddValue(ArrayItem)
            Next
            Return TimelineDiff
        End If
        Return 0
    End Function

    ''' <summary>
    ''' Iterate a Fields Child Values & Update UI
    ''' </summary>
    ''' <param name="cw"></param>
    ''' <param name="ctl"></param>
    ''' <param name="TVI"></param>
    ''' <param name="v"></param>
    ''' <param name="Parent"></param>
    ''' <returns></returns>
    Private Function IterateFieldSubValues(cw As DebugWatcher, ctl As Dictionary(Of FieldReference, DebugValueTimeline), TVI As TreeViewItem, v As DebugValue, Parent As DebugValue) As Integer
        If v.SubValues.Length > 0 Then
            Dim TimelineDiff As Integer = 0

            For i As Integer = 0 To v.SubValues.Length - 1
                Dim ArrayItem As DebugValue = v.SubValues(i)
                If ArrayItem Is Nothing Then Continue For

                If ArrayItem.Value Is Parent.Value Then
                    Continue For
                End If

                If ArrayItem.FieldReference Is CurrentTimeline Then TimelineDiff += 1

                If i > TVI.Items.Count - 1 Then
                    Dim aTVI As TreeViewItem = CreateTreeItem(cw.GUID, ArrayItem.GetType, ArrayItem.Name)

                    TimelineDiff += IterateFieldSubValues(cw, ctl, aTVI, ArrayItem, Parent)
                    If Not ctl.ContainsKey(ArrayItem.FieldReference) Then
                        Dim TL As New DebugValueTimeline(ArrayItem.FieldReference)
                        AddHandler TL.ValueChanged, Sub() ValueChangedAnim(aTVI)
                        ctl.Add(ArrayItem.FieldReference, TL)
                        TL.AddValue(ArrayItem)
                    End If
                    AddHandler aTVI.Selected, Sub() SetFieldTimeline(ArrayItem.FieldReference)
                    TVI.Items.Add(aTVI)
                Else
                    Dim aTVI As TreeViewItem = TVI.Items(i)
                    aTVI.Header = ArrayItem.Name
                    If Not ctl.ContainsKey(ArrayItem.FieldReference) Then
                        Dim TL As New DebugValueTimeline(ArrayItem.FieldReference)
                        AddHandler TL.ValueChanged, Sub() ValueChangedAnim(aTVI)
                        ctl.Add(ArrayItem.FieldReference, TL)
                        TL.AddValue(ArrayItem)
                    Else
                        ctl(ArrayItem.FieldReference).AddValue(ArrayItem)
                    End If

                    TimelineDiff += IterateFieldSubValues(cw, ctl, aTVI, ArrayItem, Parent)
                End If

            Next
            Return TimelineDiff
        End If
        Return 0
    End Function

End Class
