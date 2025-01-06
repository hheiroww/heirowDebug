Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports System.Windows
Imports System.Windows.Threading
Imports JackDebug.WPF.Values
Imports MicroSerializationLibrary.Serialization

Public Module Utils


    Public Function ForceInvoke(action As Action) As Task
        Dim frame As DispatcherFrame = New DispatcherFrame()
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, New DispatcherOperationCallback(Function(parameter)
                                                                                                                frame.[Continue] = False
                                                                                                                Return Nothing
                                                                                                            End Function), Nothing)

        Dispatcher.PushFrame(frame)
        Application.Current.Dispatcher.Invoke(action, DispatcherPriority.Send)
        'Application.Current.Dispatcher.Invoke(action, DispatcherPriority.Background)
    End Function

    Public Function SoftInvoke(action As Action) As Task
        Application.Current.Dispatcher.Invoke(action, DispatcherPriority.Send)
    End Function

    <Extension()>
    Public Function CloneObject(Of T)(Original As Object) As T
        Dim c As T = Activator.CreateInstance(GetType(T))
        Dim type As Type = GetType(T)
        For Each p As PropertyInfo In type.GetProperties()
            If p.CanRead AndAlso type.GetProperty(p.Name).CanWrite Then
                Dim val = p.GetValue(Original, Nothing)
                type.GetProperty(p.Name).SetValue(c, val, Nothing)
            End If
        Next

        For Each p As FieldInfo In type.GetFields()
            Dim val As Object = p.GetValue(Original)
            type.GetField(p.Name).SetValue(c, val)
        Next
        Return c
    End Function

    Public Function IsNothing(obj As Object) As Boolean
        Return obj Is Nothing
    End Function

    Public Function NotNothing(obj As Object) As Boolean
        Return obj IsNot Nothing
    End Function

    Public Function ResolveValue(p As PropertyReference, Instance As Object)
        If isUI() Then
            Return p.Info.GetValue(Instance)
        ElseIf DebugWatcher.EnableDispatcherProperties Then
            Dim Value As Object = Nothing
            Application.Current.Dispatcher.Invoke(Sub() Value = p.Info.GetValue(Instance))
            Return Value
        Else
            Return Nothing
        End If
    End Function

    Public Function CompareArray(CurrentValue As Object, LastValue As Object) As ArrayCompareResults
        Dim Results As New ArrayCompareResults

        Results.ValueChanged = DidValueChange(CurrentValue, LastValue)

        If Results.ValueChanged Then
            For ArrayIndex As Integer = 0 To CurrentValue.Length - 1
                Dim ArrayItem = CurrentValue(ArrayIndex)

                If LastValue Is Nothing Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf LastValue.Length = CurrentValue.Length Then
                    Dim LastArrayItem = LastValue(ArrayIndex)
                    Dim ArrayValueChanged As Boolean = DidValueChange(ArrayItem, LastArrayItem)
                    If ArrayValueChanged Then Results.ChangedIndexies.Add(ArrayIndex)
                ElseIf ArrayIndex > LastValue.Length Then
                    Results.ChangedIndexies.Add(ArrayIndex)
                End If
            Next
        End If

        Return Results
    End Function

    Public Function CompareList(CurrentValue As Object, LastValue As Object) As ArrayCompareResults
        Dim Results As New ArrayCompareResults

        Dim lastValueList As IList = Utils.CreateGenericList(LastValue)
        Dim currentValueList As IList = Utils.CreateGenericList(CurrentValue)

        Results.ValueChanged = DidValueChange(currentValueList, lastValueList)

        If Results.ValueChanged AndAlso currentValueList IsNot Nothing Then
            For ArrayIndex As Integer = 0 To currentValueList.Count - 1
                Dim ArrayItem = currentValueList(ArrayIndex)

                If lastValueList Is Nothing Then
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

    Public Function CompareDictionary(CurrentValue As Object, LastValue As Object) As ArrayCompareResults
        Dim Results As New ArrayCompareResults
        Dim isNothing As Boolean = LastValue Is Nothing
        Dim LastValueIsNothing As Boolean = If(isNothing, True, LastValue Is Nothing)
        Dim CurrentValueIsNothing As Boolean = If(isNothing, True, CurrentValue Is Nothing)
        Dim lDict As IList() = Utils.CreateGenericDictionary(LastValue)
        Dim lastListKeys As IList = lDict(0)
        Dim lastValueList As IList = lDict(1)
        Dim cDict As IList() = Utils.CreateGenericDictionary(CurrentValue)
        Dim currentKeyList As IList = cDict(0)
        Dim currentValueList As IList = cDict(1)

        Results.ValueChanged = DidValueChange(currentValueList, lastValueList)

        If Results.ValueChanged AndAlso currentValueList IsNot Nothing AndAlso lastValueList IsNot Nothing Then
            For ArrayIndex As Integer = 0 To currentValueList.Count - 1
                Dim ArrayItem = currentValueList(ArrayIndex)

                If LastValueIsNothing Then
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

    Public Function DidValueChange(CurrentValue As Object, LastValue As Object)
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

    Public Function IsImmutable(type As Type, Optional depth As Integer = 5) As Boolean
        If type Is GetType(String) OrElse type.IsValueType Then
            Return True
        ElseIf depth = 0 Then
            Return False
        Else
            Return type.GetFields().Where(Function(fInfo) Not fInfo.IsStatic).Where(Function(fInfo) fInfo.FieldType IsNot type).All(Function(fInfo) fInfo.IsInitOnly AndAlso IsImmutable(fInfo.FieldType, depth - 1)) AndAlso type.GetProperties().Where(Function(pInfo) Not pInfo.GetMethod.IsStatic).Where(Function(pInfo) pInfo.PropertyType IsNot type).All(Function(pInfo) Not SetIsAllowed(pInfo, checkNonPublicSetter:=True) AndAlso IsImmutable(pInfo.PropertyType, depth - 1))
        End If
    End Function

    <Extension()>
    Public Function CreateGenericDictionary(Dictionary As Object) As IList()
        If Dictionary IsNot Nothing Then
            Dim arguments As Type() = Dictionary.[GetType]().GetGenericArguments()
            Dim keyType = arguments(0)
            Dim valueType = arguments(1)
            Dim LockedKeys = Activator.CreateInstance(GetType(List(Of)).MakeGenericType(keyType), Dictionary.Keys)
            Dim LockedValues = Activator.CreateInstance(GetType(List(Of)).MakeGenericType(valueType), Dictionary.Values)
            Return {LockedKeys, LockedValues}
        Else
            Return Nothing
        End If
    End Function

    <Extension()>
    Public Function CreateGenericList(List As Object) As IList
        If List IsNot Nothing AndAlso List.Count > 0 Then
            Dim arguments As Type() = List.[GetType]().GetGenericArguments()
            Dim valueType = arguments(0)
            Dim LockedValues = Activator.CreateInstance(GetType(List(Of)).MakeGenericType(valueType), List)
            Return LockedValues
        Else
            Return Nothing
        End If
    End Function

    Public Function isArray(o As Object) As Boolean
        Return Information.IsArray(o)
    End Function

    Public Function IsList(o As Object) As Boolean
        If o Is Nothing Then Return False
        Return TypeOf o Is IList AndAlso o.GetType().IsGenericType AndAlso o.GetType().GetGenericTypeDefinition().IsAssignableFrom(GetType(List(Of)))
    End Function

    Public Function IsDictionary(o As Object) As Boolean
        If o Is Nothing Then Return False
        Return TypeOf o Is IDictionary AndAlso o.GetType().IsGenericType AndAlso o.GetType().GetGenericTypeDefinition().IsAssignableFrom(GetType(Dictionary(Of,)))
    End Function

    Public Function isUI() As Boolean
        Return Thread.CurrentThread.ManagedThreadId = Application.Current.Dispatcher.Thread.ManagedThreadId
    End Function

    Private Function SetIsAllowed(pInfo As PropertyInfo, Optional checkNonPublicSetter As Boolean = False) As Boolean
        Dim setMethod = pInfo.GetSetMethod(nonPublic:=checkNonPublicSetter)
        Return pInfo.CanWrite AndAlso (Not checkNonPublicSetter AndAlso setMethod.IsPublic OrElse checkNonPublicSetter AndAlso (setMethod.IsPrivate OrElse setMethod.IsFamily OrElse setMethod.IsPublic OrElse setMethod.IsAbstract))
    End Function
End Module