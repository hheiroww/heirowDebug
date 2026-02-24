Imports System.Windows.Controls
Imports JackDebug.WPF.Values
Imports MicroSerializationLibrary.Serialization

Namespace Collections
    Public Class ValueTimeline
        Implements IDisposable


        Public Shared ReadOnly Property Timelines As New Dictionary(Of String, ValueTimeline)

#Region "Properties"
        Public ReadOnly Property GUID As String
            Get
                Return _GUID
            End Get
        End Property
        Private _GUID As String

        Public ReadOnly Property Maximum As Integer
            Get
                Return _Maximum
            End Get
        End Property
        Private _Maximum As Integer = -1

        Public ReadOnly Property Right As Double
            Get
                Return _Right
            End Get
        End Property
        Private _Right As Double = 0

        Public ReadOnly Property Left As Double
            Get
                Return _Left
            End Get
        End Property
        Private _Left As Double = 0

        Public ReadOnly Property Bottom As Double
            Get
                Return _Bottom
            End Get
        End Property
        Private _Bottom As Double = 0

        Public ReadOnly Property Top As Double
            Get
                Return _Top
            End Get
        End Property
        Private _Top As Double = 0

        Public ReadOnly Property HighestValue As Object
            Get
                Return _HighestValue
            End Get
        End Property
        Private _HighestValue As Object

        Public ReadOnly Property LowestValue As Object
            Get
                Return _LowestValue
            End Get
        End Property
        Private _LowestValue As Object

        Public ReadOnly Property LastValue As DebugValue
            Get
                If Values.Length > 0 Then
                    Return Values.Last()
                Else
                    Return Nothing
                End If
            End Get
        End Property

        Public Property IsEnabled As Boolean = True

        Public Property Values As New List(Of DebugValue)
        Public Property Keys As New List(Of Integer)

#End Region

#Region "Events"

        Public Event ValueChanged(sender As ValueTimeline, ChangedValue As DebugValue, ArrayIndexies As List(Of Integer))

        Public Sub OnValueChangedEvent(ChangedValue As DebugValue, Optional ArrayIndexies As List(Of Integer) = Nothing)
            RaiseEvent ValueChanged(Me, ChangedValue, ArrayIndexies)
        End Sub

#End Region

#Region "IDisposable"
        Private disposedValue As Boolean
        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    _LowestValue.Dispose()
                    _LowestValue = Nothing
                    _HighestValue.Dispose()
                    _HighestValue = Nothing
                    Dim count As Integer = Values.Count - 1
                    For i As Integer = count To 0 Step -1
                        Values(i).Dispose()
                        Values(i) = Nothing
                    Next
                    Keys.Clear()
                    Values.Clear()
                    Values = Nothing
                    Keys = Nothing
                End If

                disposedValue = True
            End If
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
#End Region

        Public Function GetValuesWithin(StartIndex As Integer, EndIndex As Integer) As ValueTimelineSplice
            Dim LowValue As DebugValue = Nothing
            Dim HighValue As DebugValue = Nothing
            Dim OutputValues As New List(Of DebugValue)
            For i As Integer = StartIndex To EndIndex
                If Keys.Count - 1 >= StartIndex AndAlso Keys.Count - 1 >= EndIndex Then
                    Dim Value As DebugValue = Values(i)
                    OutputValues.Add(Value)
                Else
                    Exit For
                End If
            Next
            Return New ValueTimelineSplice(OutputValues)
        End Function

        Public Function GetValuesWithin(StartTime As Long, EndTime As Long) As List(Of DebugValue)
            Dim Output As New List(Of DebugValue)
            For i As Integer = 0 To Keys.Count - 1
                If Values.Count - 1 >= i Then
                    Dim Value As DebugValue = Values(i)
                    If Value.Timecode >= StartTime AndAlso Value.Timecode <= EndTime Then
                        Output.Add(Value)
                    End If
                End If
            Next
            Return Output
        End Function

        Public Function GetValuesWithin(StartTime As DateTime, EndTime As DateTime)
            Return GetValuesWithin(StartTime.Ticks, EndTime.Ticks)
        End Function

        Public Sub AddValue(value As DebugValue)
            SyncLock (Keys)
                SyncLock (Values)
                    Keys.Add(value.Timecode)
                    Values.Add(value)
                End SyncLock
            End SyncLock
            _Maximum = Keys.Count
            'If DebugValue.ValueChanged Then OnValueChangedEvent(DebugValue, DebugValue.ChangedIndexies)
            If value.Flags.isGraphable() Then
                '''TODO: Clean this area up with individual methods for low/high points.
                If value.Flags.isNumeric Then
                    If _HighestValue Is Nothing OrElse (_HighestValue < value.Value) Then
                        _HighestValue = value.Value
                    End If
                    If _LowestValue Is Nothing OrElse (_LowestValue > value.Value) Then
                        _LowestValue = value.Value
                    End If
                ElseIf value.Flags.isDrawingPoint Then
                    Dim Pt As Point = DirectCast(value.Value, Point)
                    If (Right < Pt.X) Then _Right = Pt.X
                    If (Left > Pt.X) Then _Left = Pt.X
                    If (Bottom < Pt.Y) Then _Bottom = Pt.Y
                    If (Top > Pt.Y) Then _Top = Pt.Y
                ElseIf value.Flags.isWindowsPoint Then
                    Dim Pt As System.Windows.Point = DirectCast(value.Value, System.Windows.Point)
                    If (Right < Pt.X) Then _Right = Pt.X
                    If (Left > Pt.X) Then _Left = Pt.X
                    If (Bottom < Pt.Y) Then _Bottom = Pt.Y
                    If (Top > Pt.Y) Then _Top = Pt.Y
                ElseIf value.Flags.isDrawingRectangle Then
                    Dim Rect As Rectangle = DirectCast(value.Value, Rectangle)
                    If (Right < Rect.Right) Then _Right = Rect.Right
                    If (Bottom < Rect.Bottom) Then _Bottom = Rect.Bottom
                    If (Left > Rect.Left) Then _Left = Rect.X
                    If (Top > Rect.Top) Then _Top = Rect.Y
                ElseIf value.Flags.isShapesRect Then
                    Dim Rect As System.Windows.Shapes.Rectangle = DirectCast(value.Value, System.Windows.Shapes.Rectangle)
                    If (Right < Rect.Margin.Right) Then _Right = Rect.Margin.Right
                    If (Bottom < Rect.Margin.Bottom) Then _Bottom = Rect.Margin.Bottom
                    If (Left > Rect.Margin.Left) Then _Left = Rect.Margin.Left
                    If (Top > Rect.Margin.Top) Then _Top = Rect.Margin.Top
                End If
            End If
        End Sub

        Public Function Clone() As ValueTimeline
            Dim c As ValueTimeline = CloneObject(Of ValueTimeline)
            Return c
        End Function

        Public Sub New(GUID As String, Optional FirstValue As DebugValue = Nothing)
            _GUID = GUID
            If NotNothing(FirstValue) Then AddValue(FirstValue)
        End Sub

    End Class

End Namespace