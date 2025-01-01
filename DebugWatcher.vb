Imports System.ComponentModel
Imports MicroSerializationLibrary.Serialization
Imports MicroSerializationLibrary
Imports System.Runtime.CompilerServices
Imports System.Windows.Threading
Imports System.Windows
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Input.Manipulations
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices

Public Class DebugWatcher

#Region "Caches"

    Private SubFieldReferences As New Dictionary(Of Object, FieldReference())
    Private SubPropertyReferences As New Dictionary(Of Object, PropertyReference())

    Private LastFields As New Dictionary(Of FieldReference, DebugValue)
    Private LastProperties As New Dictionary(Of PropertyReference, DebugValue)

#End Region

#Region "Events"

    Public Event ProgressChanged(Sender As DebugWatcher, e As ProgressChangedEventArgs)
    Public Event FieldsCalculated(Sender As DebugWatcher, Values As DebugValue())
    Public Event PropertiesCalculated(Sender As DebugWatcher, Values As DebugValue())

    Public Sub InvokeProgressChanged(Sender As DebugWatcher, e As ProgressChangedEventArgs)
        Progress = e.ProgressPercentage
        RaiseEvent ProgressChanged(Sender, e)
    End Sub

#End Region

#Region "Shared"

    Public Shared BlacklistedTypes As New List(Of Type)

    Public Shared Property Watchers As New List(Of DebugWatcher)

    Public Shared DebugWindow As DebugWindow

    Public Shared Sub CreateDebugWindow()
        If DebugWindow IsNot Nothing Then
            If DebugWindow.Enabled Then
                DebugWindow.Enabled = False
                DebugWindow.Close()
                DebugWindow = Nothing
            End If
        End If
        DebugWindow = New DebugWindow()
        DebugWindow.Show()
    End Sub

#End Region

#Region "Properties"

    Private Progress As Integer = 0, Index As Integer
    Public ReadOnly Property GUID As Guid = Guid.NewGuid()
    Public Property ValueCount As Integer = 0
    Public Property EnableDispatcherProperties As Boolean = False

    Public ReadOnly Property Name As String
        Get
            Return _Name
        End Get
    End Property
    Private _Name As String

    Public ReadOnly Property AttachedObject
        Get
            Return _AttachedObject
        End Get
    End Property
    Private _AttachedObject As Object

    Public Property Recursive As Boolean
        Get
            Return _Recursive
        End Get
        Set(value As Boolean)
            _Recursive = value
        End Set
    End Property
    Private _Recursive As Boolean = False

    Public Property isEnabled As Boolean
        Get
            Return _isEnabled
        End Get
        Set(value As Boolean)
            _isEnabled = value
            If _isEnabled Then
                BackgroundWorker.RunWorkerAsync()
            Else
                Index = 0
                Progress = 0
            End If
        End Set
    End Property
    Private _isEnabled As Boolean = False

    Public ReadOnly Property Fields As FieldReference()
        Get
            Return _Fields
        End Get
    End Property
    Private _Fields As FieldReference()

    Public ReadOnly Property Properties As PropertyReference()
        Get
            Return _Properties
        End Get
    End Property
    Private _Properties As PropertyReference()

    Public ReadOnly Property isProperty As Boolean
        Get
            Return _isProperty
        End Get
    End Property
    Private _isProperty As Boolean = False

#End Region

#Region "Initializers"

    Public Sub New(AttachedTo As Object)
        Initialize(AttachedTo)
    End Sub

    Public Sub New(AttachedTo As Object, Recursive As Boolean)
        _Recursive = Recursive
        Initialize(AttachedTo)
    End Sub

    Private Sub Initialize(ByRef AttachedTo As Object, Optional Name As String = "")
        _Name = If(Name = "", AttachedTo.GetType().ToString(), Name)
        _AttachedObject = AttachedTo
        DeserializationWrapper.ReflectionFlags = BindingFlags.Instance Or BindingFlags.[Public] Or BindingFlags.NonPublic
        _Fields = DeserializationWrapper.GetFieldReferences(AttachedObject).ToArray()
        _Properties = DeserializationWrapper.GetPropertyReferences(AttachedObject).ToArray()

        ValueCount = If(Fields IsNot Nothing, _Fields.Count, 0) + If(Properties IsNot Nothing, _Properties.Count, 0)
        DebugWatcher.Watchers.Add(Me)
    End Sub

#End Region

#Region "Background Worker"

    Private WithEvents BackgroundWorker As New BackgroundWorker With {.WorkerSupportsCancellation = True}

    Private Sub BackgroundWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles BackgroundWorker.DoWork
        Do While isEnabled
            Dim DateTime As DateTime = DateTime.Now
            Dim CurrentTime As String = DateTime.ToShortDateString() & " " & DateTime.ToShortTimeString()
            Progress = 0
            Index = 0

            ValueCount = Fields.Length + Properties.Length

            InvokeProgressChanged(Me, New ProgressChangedEventArgs(Progress, {0, TimeSpan.Zero}))

            RaiseEvent FieldsCalculated(Me, GetFieldValues(Fields, AttachedObject))
            RaiseEvent PropertiesCalculated(Me, GetPropertyValues(Properties, AttachedObject))
        Loop
    End Sub

#End Region

#Region "General Logic"

    Public Sub StartSession()
        If Not isEnabled Then isEnabled = True
    End Sub

    Public Sub EndSession()
        If isEnabled Then isEnabled = False
    End Sub

#End Region

    Private Function GetLastFieldValue(f As FieldReference) As DebugValue
        If LastFields IsNot Nothing AndAlso LastFields.ContainsKey(f) Then
            Return LastFields(f)
        End If
        Return Nothing
    End Function

    Private Function GetLastPropertyValue(p As PropertyReference) As DebugValue
        If LastProperties IsNot Nothing AndAlso LastProperties.ContainsKey(p) Then
            Return LastProperties(p)
        End If
        Return Nothing
    End Function

    Private Sub UpdateLastFieldValue(f As FieldReference, dv As DebugValue)
        If LastFields.ContainsKey(f) Then
            LastFields(f) = dv
        Else
            LastFields.Add(f, dv)
        End If
    End Sub

    Private Sub UpdateLastPropertyValue(p As PropertyReference, dv As DebugValue)
        If LastProperties.ContainsKey(p) Then
            LastProperties(p) = dv
        Else
            LastProperties.Add(p, dv)
        End If
    End Sub

    Private Function GetFieldSubValues(Flags As TypeFlags, CurrentValue As Object, LastValue As DebugValue, Instance As Object, ParentInstance As Object) As DebugValue()
        If Not Flags.isSystem AndAlso Not (Flags.isArray Or Flags.isList Or Flags.isDictionary) AndAlso Recursive AndAlso CurrentValue IsNot Nothing AndAlso CurrentValue IsNot AttachedObject Then
            If Not SubFieldReferences.ContainsKey(CurrentValue) Then
                If LastValue IsNot Nothing AndAlso LastValue.Value IsNot Nothing AndAlso SubFieldReferences.ContainsKey(LastValue.Value) Then
                    Dim ObjectFields As FieldReference() = SubFieldReferences(LastValue.Value)
                    SubFieldReferences.Add(CurrentValue, ObjectFields)
                    SubFieldReferences.Remove(LastValue.Value)
                    Return GetFieldValues(ObjectFields, CurrentValue)
                Else
                    Dim ObjectFields As FieldReference() = DeserializationWrapper.GetFieldReferences(CurrentValue).ToArray()
                    SubFieldReferences.Add(CurrentValue, ObjectFields)
                    Return GetFieldValues(ObjectFields, CurrentValue)
                End If
            ElseIf CurrentValue IsNot Nothing AndAlso CurrentValue IsNot ParentInstance Then
                Return GetFieldValues(SubFieldReferences(CurrentValue), CurrentValue, Instance)
            End If
        End If
        Return Nothing
    End Function

    Private Function GetPropertySubValues(Flags As TypeFlags, CurrentValue As Object, LastValue As DebugValue, Instance As Object, ParentInstance As Object) As DebugValue()
        If Not Flags.isSystem AndAlso Not Flags.isArray AndAlso Recursive AndAlso Recursive AndAlso CurrentValue IsNot Nothing AndAlso CurrentValue IsNot AttachedObject Then
            If Not SubPropertyReferences.ContainsKey(CurrentValue) Then
                If LastValue IsNot Nothing AndAlso LastValue.Value IsNot Nothing AndAlso SubPropertyReferences.ContainsKey(LastValue.Value) Then
                    Dim ObjectFields As PropertyReference() = SubPropertyReferences(LastValue.Value)
                    SubPropertyReferences.Add(CurrentValue, ObjectFields)
                    SubPropertyReferences.Remove(LastValue.Value)
                    Return GetPropertyValues(ObjectFields, CurrentValue)
                Else
                    Dim ObjectProperties As PropertyReference() = DeserializationWrapper.GetPropertyReferences(CurrentValue).ToArray()
                    SubPropertyReferences.Add(CurrentValue, ObjectProperties)
                    Return GetPropertyValues(ObjectProperties, CurrentValue)
                End If
            ElseIf CurrentValue IsNot Nothing AndAlso CurrentValue IsNot ParentInstance Then
                Return GetPropertyValues(SubPropertyReferences(CurrentValue), CurrentValue, Instance)
            End If
        End If
        Return Nothing
    End Function

    Private Function DidValueChange(CurrentValue As Object, LastValue As Object)
        Dim cValIsNothing As Boolean = CurrentValue Is Nothing
        Dim lValIsNothing As Boolean = LastValue Is Nothing
        If cValIsNothing AndAlso Not lValIsNothing Then
            Return True
        ElseIf lValIsNothing AndAlso Not cValIsNothing Then
            Return True
        ElseIf cValIsNothing AndAlso lValIsNothing Then
            Return False
        ElseIf Not CurrentValue.Equals(LastValue) Then
            Return True
        End If
        Return False
    End Function

    Private Function CompareArray(CurrentValue As Object, LastValue As DebugValue) As ArrayCompareResults
        Dim Results As New ArrayCompareResults

        Results.ValueChanged = DidValueChange(CurrentValue, LastValue)

        If Results.ValueChanged Then
            For ArrayIndex As Integer = 0 To CurrentValue.Length - 1
                Dim ArrayItem = CurrentValue(ArrayIndex)

                If LastValue Is Nothing OrElse LastValue.Value Is Nothing Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf LastValue.Value.Length = CurrentValue.Length Then
                    Dim LastArrayItem = LastValue.Value(ArrayIndex)
                    Dim ArrayValueChanged As Boolean = DidValueChange(ArrayItem, LastArrayItem)
                    If ArrayValueChanged Then Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf ArrayIndex > LastValue.Value.Length Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                End If
            Next
        End If

        Return Results
    End Function

    Private Function CompareList(CurrentValue As Object, LastValue As DebugValue) As ArrayCompareResults
        Dim Results As New ArrayCompareResults

        Dim lastValueList As IList = If(LastValue Is Nothing, Nothing, LastValue.Value)
        Dim currentValueList As IList = Utils.CreateGenericList(CurrentValue)

        Results.ValueChanged = DidValueChange(currentValueList, lastValueList)

        If Results.ValueChanged AndAlso currentValueList IsNot Nothing Then
            For ArrayIndex As Integer = 0 To currentValueList.Count - 1
                Dim ArrayItem = currentValueList(ArrayIndex)

                If LastValue Is Nothing OrElse LastValue.Value Is Nothing Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf lastValueList.Count = currentValueList.Count Then
                    Dim LastArrayItem = lastValueList(ArrayIndex)
                    Dim ArrayValueChanged As Boolean = DidValueChange(ArrayItem, LastArrayItem)
                    If ArrayValueChanged Then Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf ArrayIndex > lastValueList.Count Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                End If
            Next
        End If

        Return Results
    End Function

    Private Function CompareDictionary(CurrentValue As Object, LastValue As DebugValue) As ArrayCompareResults
        Dim Results As New ArrayCompareResults
        Dim isNothing As Boolean = LastValue Is Nothing
        Dim LastValueIsNothing As Boolean = If(isNothing, True, LastValue.Value Is Nothing)
        Dim CurrentValueIsNothing As Boolean = If(isNothing, True, CurrentValue Is Nothing)
        Dim lastListKeys As IList = If(LastValue IsNot Nothing, LastValue.KeyList, Nothing)
        Dim lastValueList As IList = If(LastValue IsNot Nothing, LastValue.ValueList, Nothing)
        Dim cDict As IList() = Utils.CreateGenericDictionary(CurrentValue)
        Dim currentKeyList As IList = cDict(0)
        Dim currentValueList As IList = cDict(1)

        Results.ValueChanged = DidValueChange(currentValueList, lastValueList)

        If Results.ValueChanged AndAlso currentValueList IsNot Nothing AndAlso lastValueList IsNot Nothing Then
            For ArrayIndex As Integer = 0 To currentValueList.Count - 1
                Dim ArrayItem = currentValueList(ArrayIndex)

                If LastValue Is Nothing OrElse LastValue.Value Is Nothing Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf lastValueList.Count = currentValueList.Count Then
                    Dim LastArrayItem = lastValueList(ArrayIndex)
                    Dim ArrayValueChanged As Boolean = DidValueChange(ArrayItem, LastArrayItem)
                    If ArrayValueChanged Then Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf ArrayIndex > lastValueList.Count Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                End If
            Next
        End If
        Return Results
    End Function

    Private Function GetFieldValues(Fields As FieldReference(), Instance As Object, Optional ParentInstance As Object = Nothing) As DebugValue()
        Dim L As DebugValue() = New DebugValue(Fields.Count) {}
        If Fields Is Nothing Then Return L

        For i As Integer = 0 To Fields.Length - 1
            Dim f As FieldReference = Fields(i)

            If Instance.GetType() Is Me.GetType() Then Continue For
            Dim StartTime As DateTime = DateTime.Now
            Dim CurrentValue As Object = f.Info.GetValue(Instance)

            Dim t As Type = f.Info.FieldType
            Dim Flags As TypeFlags = DebugValue.GetTypeFlags(CurrentValue, t)
            Dim ValueChanged As Boolean = False
            Dim LastValue As DebugValue = GetLastFieldValue(f)
            Dim ChangedIndexies As Integer() = Nothing
            If Flags.isArray Then
                Dim Results As ArrayCompareResults = CompareArray(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isList Then
                Dim Results As ArrayCompareResults = CompareList(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isDictionary Then
                Dim Results As ArrayCompareResults = CompareDictionary(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Not DebugValue.IsIgnored(t) Then
                ValueChanged = DidValueChange(CurrentValue, If(LastValue IsNot Nothing, LastValue.Value, Nothing))
            End If

            Dim FieldSubValues As DebugValue() = GetFieldSubValues(Flags, CurrentValue, LastValue, Instance, ParentInstance)

            Dim EndTime As DateTime = DateTime.Now
            Dim ElapsedTime As TimeSpan = TimeSpan.FromMilliseconds((EndTime - StartTime).TotalMilliseconds)
            Dim p As Double = System.Math.Round(Index / ValueCount * 100, 2)
            InvokeProgressChanged(Me, New ProgressChangedEventArgs(p, {"[" & Name & "] Field #" & Index & " Complete. ", ElapsedTime.ToString()}))

            If ValueChanged Then
                Dim dv As New DebugValue(f.Info.Name, f, Nothing, t, Flags, CurrentValue, ValueChanged, LastValue, FieldSubValues, ChangedIndexies)
                UpdateLastFieldValue(f, dv)
                L(i) = dv
            ElseIf Not LastFields.ContainsKey(f) Then
                Dim dv As New DebugValue(f.Info.Name, f, Nothing, t, Flags, CurrentValue, ValueChanged, LastValue, FieldSubValues, ChangedIndexies)
                UpdateLastFieldValue(f, dv)
                L(i) = dv
            Else
                L(i) = LastFields(f).SetValueChanged(False).SetSubValues(FieldSubValues).SetChangedIndexies(ChangedIndexies).SetFlags(Flags)
            End If

            Index += 1
        Next

        Return L
    End Function

    Private Function ResolveValue(p As PropertyReference, Instance As Object)
        If isUI() Then
            Return p.Info.GetValue(Instance)
        ElseIf EnableDispatcherProperties Then
            Dim Value As Object = Nothing
            Application.Current.Dispatcher.Invoke(Sub() Value = p.Info.GetValue(Instance))
            Return Value
        Else
            Return Nothing
        End If
    End Function

    Private Function GetPropertyValues(Properties As PropertyReference(), Instance As Object, Optional ParentInstance As Object = Nothing) As DebugValue()
        Dim L As DebugValue() = New DebugValue(Properties.Count) {}
        If Properties Is Nothing Then Return L
        For i As Integer = 0 To Properties.Count - 1
            Dim p As PropertyReference = Properties(i)
            If Instance.GetType() Is Me.GetType() Then Continue For
            Dim StartTime As DateTime = DateTime.Now
            Dim CurrentValue As Object = ResolveValue(p, Instance)
            Dim t As Type = p.Info.PropertyType
            Dim Flags As TypeFlags = DebugValue.GetTypeFlags(CurrentValue, t)
            Dim ValueChanged As Boolean = False
            Dim LastValue As DebugValue = GetLastPropertyValue(p)
            Dim isIgnored As Boolean = DebugValue.IsIgnored(t)
            Dim ChangedIndexies As Integer() = Nothing

            If Flags.isArray Then
                Dim Results As ArrayCompareResults = CompareArray(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isList Then
                Dim Results As ArrayCompareResults = CompareList(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isDictionary Then
                Dim Results As ArrayCompareResults = CompareDictionary(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Not isIgnored Then
                ValueChanged = DidValueChange(CurrentValue, If(LastValue IsNot Nothing, LastValue.Value, Nothing))
            End If

            Dim PropertySubValues As DebugValue() = GetPropertySubValues(Flags, CurrentValue, LastValue, Instance, ParentInstance)
            Dim EndTime As DateTime = DateTime.Now
            Dim ElapsedTime As TimeSpan = TimeSpan.FromMilliseconds((EndTime - StartTime).TotalMilliseconds)
            InvokeProgressChanged(Me, New ProgressChangedEventArgs(System.Math.Round(Index / ValueCount * 100, 2), {"[" & Name & "] Property #" & Index & " Complete. ", ElapsedTime.ToString()}))
            If ValueChanged Then
                Dim dv As New DebugValue(p.Info.Name, Nothing, p, t, Flags, CurrentValue, ValueChanged, LastValue, PropertySubValues, ChangedIndexies)
                L(i) = dv
                If LastProperties.ContainsKey(p) Then
                    LastProperties(p) = dv
                Else
                    LastProperties.Add(p, dv)
                End If
            ElseIf Not LastProperties.ContainsKey(p) Then
                Dim dv As New DebugValue(p.Info.Name, Nothing, p, t, Flags, CurrentValue, ValueChanged, LastValue, PropertySubValues, ChangedIndexies)
                If LastProperties.ContainsKey(p) Then
                    LastProperties(p) = dv
                Else
                    LastProperties.Add(p, dv)
                End If
                L(i) = dv
            Else
                L(i) = LastProperties(p).SetValueChanged(False).SetSubValues(PropertySubValues).SetChangedIndexies(ChangedIndexies)
            End If
            Index += 1
        Next
        Return L
    End Function

End Class
