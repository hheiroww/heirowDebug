Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Documents
Imports System.Windows.Input

Public Module ResizableArray

    <Extension()>
    Public Sub Add(Of T)(ByRef arr As T(), item As T)
        Array.Resize(arr, arr.Length + 1)
        arr(arr.Length - 1) = item
    End Sub

    <Extension()>
    Public Sub AddRange(Of T)(ByRef arr As T(), item As T())
        For i As Integer = 0 To item.Length - 1
            arr.Add(item(i))
        Next
    End Sub

    <Extension()>
    Public Sub Remove(Of T)(ByRef arr As T(), item As T)
        For i As Integer = 0 To arr.Length - 1
            If arr(i).Equals(item) Then
                arr.RemoveAt(i)
                Exit For
            End If
        Next
    End Sub

    <Extension()>
    Public Sub RemoveAll(Of T)(ByRef arr As T(), item As T)
        For i As Integer = 0 To arr.Length - 1
            If arr(i).Equals(item) Then
                arr.RemoveAt(i)
            End If
        Next
    End Sub

    <Extension()>
    Public Sub RemoveAt(Of T)(ByRef arr As T(), index As Integer)
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        Dim arrLen = uBound - lBound

        If index < lBound OrElse index > uBound Then
            Throw New ArgumentOutOfRangeException(
            String.Format("Index must be from {0} to {1}.", lBound, uBound))
        Else
            Dim outArr(arrLen - 1) As T
            Array.Copy(arr, 0, outArr, 0, index)
            Array.Copy(arr, index + 1, outArr, index, uBound - index)
            arr = outArr
        End If
    End Sub


    <Extension()>
    Public Sub InsertAt(Of T)(ByRef arr As T(), item As T, index As Integer)
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        Dim arrLen = uBound - lBound

        If index < lBound OrElse index > uBound Then
            Throw New ArgumentOutOfRangeException(
            String.Format("Index must be from {0} to {1}.", lBound, uBound))
        Else
            Dim outArr(arrLen + 1) As T
            Array.Copy(arr, 0, outArr, 0, index)
            outArr.Add(item)
            Array.Copy(arr, index + 1, outArr, index, uBound - index)
            arr = outArr
        End If
    End Sub

    <Extension()>
    Public Function Combine(Of T)(A As T(), B As T()) As T()
        Return A.Union(B)
    End Function

    <Extension()>
    Public Sub CombineWith(Of T)(ByRef A As T(), B As T())
        A = A.Union(B)
    End Sub

End Module

Public Module GenericExtensions

    <Extension()>
    Public Function Length(Of T)(l As List(Of T)) As Integer
        Return l.Count
    End Function

    <Extension()>
    Public Function Length(Of TKey, TValue)(d As Dictionary(Of TKey, TValue)) As Integer
        Return d.Count
    End Function

    Public Function ElementAt(Of TKey, TValue)(Dictionary As Dictionary(Of TKey, TValue), Index As Integer) As KeyValuePair(Of TKey, TValue)
        Return Dictionary.ElementAt(Index)
    End Function

    Public Function KeyElementAt(Of TKey, TValue)(Dictionary As Dictionary(Of TKey, TValue), Index As Integer) As TKey
        Return Dictionary.Keys.ElementAt(Index)
    End Function

    Public Function ValueElementAt(Of TKey, TValue)(Dictionary As Dictionary(Of TKey, TValue), Index As Integer) As TValue
        Return Dictionary.Values.ElementAt(Index)
    End Function

    Public Function CloneDictionary(Of TKey, TValue As ICloneable)(original As Dictionary(Of TKey, TValue)) As Dictionary(Of TKey, TValue)
        Dim ret As Dictionary(Of TKey, TValue) = New Dictionary(Of TKey, TValue)(original.Count, original.Comparer)
        For Each entry In original
            ret.Add(entry.Key, CType(entry.Value.Clone(), TValue))
        Next
        Return ret
    End Function

    Public Function ToArray(source As IEnumerable, Length As Integer) As Object()
        Dim items As Object() = Nothing
        Dim count = 0

        For i As Integer = 0 To Length - 1
            Dim item = source(i)
            If items Is Nothing Then
                items = New Object(3) {}
            ElseIf items.Length = count Then
                Dim destinationArray2 = New Object(count * 2 - 1) {}
                Array.Copy(items, 0, destinationArray2, 0, count)
                items = destinationArray2
            End If
            items(count) = item
            count += 1
        Next

        If items.Length = count Then
            Return items
        End If

        Dim destinationArray As Object() = New Object(count - 1) {}
        Array.Copy(items, 0, destinationArray, 0, count)
        Return destinationArray
    End Function

    Public Function Interpolate(ByVal i As Double, ByVal minimum_value As Double, ByVal maximum_value As Double, ByVal begin As Double, ByVal enddbl As Double) As Double
        Return begin + (enddbl - begin) / (maximum_value - minimum_value) * (i - minimum_value)
    End Function

End Module