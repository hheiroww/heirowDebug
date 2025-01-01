Imports System.Collections.Specialized
Imports System.Windows
Imports MicroSerializationLibrary.Serialization

Public Class DebugValue

#Region "Properties"

    Public ReadOnly Property Name As String
        Get
            Return _Name
        End Get
    End Property
    Private _Name As String

    Public ReadOnly Property Type As Type
        Get
            Return _Type
        End Get
    End Property
    Private _Type As Type

    Public ReadOnly Property Value As Object
        Get
            Return _Value
        End Get
    End Property
    Private _Value As Object

    Public ReadOnly Property LastValue As Object
        Get
            Return _LastValue
        End Get
    End Property
    Private _LastValue As Object

    Public ReadOnly Property ChangedIndexies As Integer()
        Get
            Return _ChangedIndexies
        End Get
    End Property
    Private _ChangedIndexies As Integer() = Nothing

    Public ReadOnly Property KeyList As IList
        Get
            Return _KeyList
        End Get
    End Property
    Private _KeyList As IList

    Public ReadOnly Property ValueList As IList
        Get
            Return _ValueList
        End Get
    End Property
    Private _ValueList As IList

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

    Public ReadOnly Property SubValues As DebugValue()
        Get
            Return _SubValues
        End Get
    End Property
    Private _SubValues As DebugValue() = New DebugValue() {}

    Public ReadOnly Property ValueChanged As Boolean
        Get
            Return _ValueChanged
        End Get
    End Property
    Private _ValueChanged As Boolean

    Public ReadOnly Property Size As Long
        Get
            If _Size = -1 Then
                Using ms As New IO.MemoryStream
                    Dim bf As New Runtime.Serialization.Formatters.Binary.BinaryFormatter()
                    bf.Serialize(ms, Value)
                    _Size = ms.Position
                End Using
            End If
            Return _Size
        End Get
    End Property
    Private _Size As Long = -1

    Public ReadOnly Property IsField As Boolean
        Get
            Return _IsField
        End Get
    End Property
    Private _IsField As Boolean = False

    Public ReadOnly Property IsProperty As Boolean
        Get
            Return _IsProperty
        End Get
    End Property
    Private _IsProperty As Boolean = False

    Public ReadOnly Property Flags As TypeFlags
        Get
            Return _Flags
        End Get
    End Property
    Private _Flags As TypeFlags

    Public ReadOnly Property Length As Integer
        Get
            If Flags.isArray Then
                Return Value.Length
            ElseIf Flags.isDictionary Or Flags.isList Then
                Return Value.Count
            Else
                Return -1
            End If
        End Get
    End Property

    Public ReadOnly Property Timecode As Long
        Get
            Return _Timecode
        End Get
    End Property
    Private _Timecode As Long

    Public ReadOnly Property isNothing
        Get
            Return _isNothing
        End Get
    End Property
    Private _isNothing As Boolean

    Public ReadOnly Property isXAML As Boolean
        Get
            Return _isXAML
        End Get
    End Property
    Private _isXAML As Boolean

    Public ReadOnly Property isGraphable As Boolean
        Get
            Return Flags.isNumeric Or Flags.isBoolean AndAlso Not isNothing
        End Get
    End Property

#End Region

#Region "Shared"

    Public Shared IgnoreTypes As Type() = New Type() {GetType(Bitmap)}

    Public Shared Function IsIgnored(Type As Type) As Boolean
        Return IgnoreTypes.Contains(Type)
    End Function

#End Region

#Region "Events"

    Public Event OnValueChanged(OldVal, NewVal)

    Public Sub InvokeValueChanged(OldVal, NewVal)
        _ValueChanged = True
        RaiseEvent OnValueChanged(OldVal, NewVal)
    End Sub

#End Region

#Region "Public Functions"

    Public Function SetValueChanged(ValueChanged As Boolean) As DebugValue
        _ValueChanged = ValueChanged
        Return Me
    End Function

    Public Function SetChangedIndexies(ChangedIndexies As Integer()) As DebugValue
        _ChangedIndexies = ChangedIndexies
        Return Me
    End Function

    Public Function SetSubValues(SubValues As DebugValue()) As DebugValue
        If SubValues IsNot Nothing Then _SubValues = SubValues
        Return Me
    End Function

#End Region

#Region "General Logic"

    Private Sub CheckIsNothing()
        If IgnoreTypes.Contains(Type) Then
            _isNothing = True
        Else
            _Value = Value
            _isNothing = _Value Is Nothing
        End If
    End Sub

#End Region

    Public Sub New(Name As String, FieldReference As FieldReference, PropertyReference As PropertyReference, Type As Type, Value As Object,
                     Optional ValueChanged As Boolean = False,
                     Optional LastValue As Object = Nothing,
                     Optional SubValues As DebugValue() = Nothing,
                     Optional ChangedIndexies As Integer() = Nothing)

        _Name = Name
        _Type = Type
        CheckIsNothing()
        _LastValue = LastValue
        _ValueChanged = ValueChanged
        _IsField = FieldReference IsNot Nothing
        _IsProperty = PropertyReference IsNot Nothing
        _FieldReference = FieldReference
        _PropertyReference = PropertyReference
        _ChangedIndexies = ChangedIndexies
        _isXAML = _Type Is GetType(DependencyObject)
        _SubValues = If(SubValues Is Nothing, New DebugValue() {}, SubValues)
        _Timecode = DateTime.UtcNow().Ticks

        If Flags.isDictionary Then
            Dim dict As IList() = Utils.CreateGenericDictionary(Value)
            _KeyList = dict(0)
            _ValueList = dict(1)
        ElseIf Flags.isList Then
            _ValueList = Utils.CreateGenericList(Value)
        End If
    End Sub


End Class