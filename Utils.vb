Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports System.Windows

Public Module Utils

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