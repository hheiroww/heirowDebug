Imports MicroSerializationLibrary.Serialization
Imports System.Windows.Controls

Public Class WatcherDictionaryItem

    Public Property TreeViewItem As TreeViewItem

    Public Property Fields As New Dictionary(Of FieldReference, TreeViewItem)
    Public Property Properties As New Dictionary(Of PropertyReference, TreeViewItem)

    Public Sub New(TVI As TreeViewItem)
        TreeViewItem = TVI
    End Sub

End Class