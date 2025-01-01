Imports MicroSerializationLibrary.Serialization

Public Class DebugValueTimeline

    Public Event ValueChanged(sender As DebugValueTimeline, ArrayIndexies As Integer())

    Public ReadOnly Property FieldReference As FieldReference
        Get
            Return _FieldReference
        End Get
    End Property
    Private _FieldReference As FieldReference

    Public ReadOnly Property PropertyReference As PropertyReference
        Get
            Return _PropertyReference
        End Get
    End Property
    Private _PropertyReference As PropertyReference

    Public ReadOnly Property Maximum As Integer
        Get
            Return _Maximum
        End Get
    End Property
    Private _Maximum As Integer = -1

    Public ReadOnly Property HighestValue As DebugValue
        Get
            Return _HighestValue
        End Get
    End Property
    Private _HighestValue As DebugValue

    Public ReadOnly Property LowestValue As DebugValue
        Get
            Return _LowestValue
        End Get
    End Property
    Private _LowestValue As DebugValue

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

    Public Values As DebugValue() = New DebugValue() {}
    Private Keys As Long() = New Long() {}

    Public Sub OnValueChangedEvent(Optional ArrayIndexies As Integer() = Nothing)
        RaiseEvent ValueChanged(Me, ArrayIndexies)
    End Sub

    Public Function GetValuesWithin(StartIndex As Integer, EndIndex As Integer) As TimelineSplice
        Dim LowValue As DebugValue = Nothing
        Dim HighValue As DebugValue = Nothing
        Dim OutputValues As DebugValue() = New DebugValue() {}
        For i As Integer = StartIndex To EndIndex
            If Keys.Length - 1 >= StartIndex AndAlso Keys.Length - 1 >= EndIndex Then
                Dim Value As DebugValue = Values(i)
                If Value.isGraphable Then
                    If LowValue Is Nothing OrElse Value.Value < LowValue.Value Then LowValue = Value
                    If HighValue Is Nothing OrElse Value.Value > HighValue.Value Then HighValue = Value
                End If
                OutputValues.Add(Value)
            End If
        Next
        Return New TimelineSplice(OutputValues, LowValue, HighValue)
    End Function

    Public Function GetValuesWithin(StartTime As Long, EndTime As Long) As DebugValue()
        Dim Output As DebugValue() = New DebugValue() {}
        For i As Integer = 0 To Keys.Length - 1
            If Values.Length - 1 >= i Then
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

    Public Sub AddValue(DebugValue As DebugValue)
        Keys.Add(DebugValue.Timecode)
        Values.Add(DebugValue)
        _Maximum = Keys.Length
        If DebugValue.isGraphable Then
            If _HighestValue Is Nothing OrElse (_HighestValue.Value < DebugValue.Value) Then _HighestValue = DebugValue
            If _LowestValue Is Nothing OrElse (_LowestValue.Value > DebugValue.Value) Then _LowestValue = DebugValue
        End If
    End Sub

    Public Sub New(PropertyReference As PropertyReference)
        _PropertyReference = PropertyReference
    End Sub

    Public Sub New(FieldReference As FieldReference)
        _FieldReference = FieldReference
    End Sub

End Class

Public Class TimelineSplice

    Public ReadOnly Property Values As DebugValue()
        Get
            Return _Values
        End Get
    End Property
    Private _Values As DebugValue()

    Public ReadOnly Property HighestValue As DebugValue
        Get
            Return _HighestValue
        End Get
    End Property
    Private _HighestValue As DebugValue

    Public ReadOnly Property LowestValue As DebugValue
        Get
            Return _LowestValue
        End Get
    End Property
    Private _LowestValue As DebugValue

    Public ReadOnly Property isGraphable As Boolean
        Get
            Return _isGraphable
        End Get
    End Property
    Private _isGraphable As Boolean

    Public Sub New(Values As DebugValue(), LowValue As DebugValue, HighValue As DebugValue)
        _Values = Values
        _LowestValue = LowValue
        _HighestValue = HighValue
        _isGraphable = Values.Length > 0 AndAlso Values.First().isGraphable
    End Sub
End Class
