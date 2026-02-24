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
Imports JackDebug.WPF.Collections
Imports JackDebug.WPF.Values
Imports MahApps.Metro.Controls
Imports SocketJack
Imports SocketJack.Serialization
Imports System.Linq

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
        If e.NewValue.GetType IsNot GetType(TreeViewItem) Then Return
        Dim TreeItem As TreeViewItem = DirectCast(e.NewValue, TreeViewItem)
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
                If val.Type IsNot Nothing AndAlso Not val.Type.IsValueType AndAlso Not val.Flags.isString AndAlso Not watcher.ChildWatcherValues.Contains(tag) Then
                    _selectedValueGuid = tag
                    ShowAddWatchButton()
                    Return
                End If
            End If

            _selectedValueGuid = Nothing
            HideAddWatchButton()
            SetTimeline(tag)
        End If
    End Sub

    Private Sub DebugWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        Enabled = False
        BackgroundWorker.Dispose()
        Dim watchersToDispose = DebugWatcher.Watchers.Values.ToList()
        For Each w In watchersToDispose
            w.Dispose()
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
    Private ValueOwnerLookup As New Dictionary(Of String, Tuple(Of DebugValue, DebugWatcher))
    Private _selectedValueGuid As String

#End Region

#Region "Drawing"

    Public Sub ClearPoints()
        InvokeUI(Sub()
                     Points.Clear()
                     SpatialCanvas.Children.Clear()
                     SpatialAxisLeft.Children.Clear()
                     SpatialAxisBottom.Children.Clear()
                     StringValueTextBox.Visibility = Visibility.Collapsed
                     StringValueTextBox.Text = ""
                 End Sub)
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
        If Guid Is Nothing Then Return
        If ValueTimeline.Timelines.ContainsKey(Guid) Then
            CurrentTimeline = ValueTimeline.Timelines(Guid)
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

    Private Sub HideAddWatchButton()
        InvokeUI(Sub()
                     AddWatchButton.Visibility = Visibility.Collapsed
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

        Dim child As New DebugWatcher(watcher, _selectedValueGuid, actualValue, True)
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
    ''' <summary>
    ''' Tree View Item Updates
    ''' </summary>
    Private Async Sub BackgroundWorker_DoWork(state As Object)
        Dim ResultInterval As TimeSpan
        Dim ms As Double = 0

        Do While Enabled
            Dim nCount As Integer = WatcherCount
            If nCount <> InitializedWatchers.Count Then
                Dim c As Integer = 0
                SyncLock (InitializedWatchers)
                    c = DebugWatcher.Watchers.Count - 1
                End SyncLock
                For i As Integer = 0 To c
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
                                             If InitializedWatchers.ContainsKey(w.Guid) Then Return
                                             InitializedWatchers.Add(w.Guid, w)
                                             AddHandler w.ValueCalculated, AddressOf ValueCalculated
                                             AddHandler w.ValueChanged, AddressOf ValueChanged
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

    Private Async Sub UpdateDictionaryTVI(TVI As TreeViewItem, Value As DebugValue)
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

    Private Sub UpdateItemProperties(aTVI As TreeViewItem, item As Object)
        If item Is Nothing Then Return

        Dim itemType As Type = item.GetType()
        If IsSystemType(itemType) Then Return
        If GetType(System.Windows.Threading.DispatcherObject).IsAssignableFrom(itemType) Then Return

        Dim headerTexts As New List(Of String)
        Dim toolTips As New List(Of String)
        Try
            For Each f As FieldInfo In itemType.GetFields(BindingFlags.Instance Or BindingFlags.Public)
                If DebugValue.IgnoreTypes.Contains(f.FieldType) Then Continue For
                Try
                    Dim fVal As Object = f.GetValue(item)
                    Dim valueStr As String = If(IsNothing(fVal), "NULL", Me.ValueToString(fVal.ToString()))
                    headerTexts.Add(f.Name & ": " & valueStr)
                    toolTips.Add(f.FieldType.Name)
                Catch
                End Try
            Next

            For Each p As PropertyInfo In itemType.GetProperties(BindingFlags.Instance Or BindingFlags.Public)
                If Not p.CanRead Then Continue For
                If p.GetIndexParameters().Length > 0 Then Continue For
                If DebugValue.IgnoreTypes.Contains(p.PropertyType) Then Continue For
                Try
                    Dim pVal As Object = p.GetValue(item)
                    Dim valueStr As String = If(IsNothing(pVal), "NULL", Me.ValueToString(pVal.ToString()))
                    headerTexts.Add(p.Name & ": " & valueStr)
                    toolTips.Add(p.PropertyType.Name)
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
                                 childTVI.Background = New SolidColorBrush(DefaultBackground)
                                 aTVI.Items.Add(childTVI)
                                 ValueChangedAnim(childTVI)
                             Else
                                 UpdateHeader(DirectCast(aTVI.Items(idx), TreeViewItem), headerTexts(idx))
                             End If
                         Next

                         While aTVI.Items.Count > headerTexts.Count
                             aTVI.Items.RemoveAt(aTVI.Items.Count - 1)
                         End While
                     Catch
                     End Try
                 End Sub)
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
                InvokeUI(Sub() UpdateHeader(GetTVI(Value), Header))
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
