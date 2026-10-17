Imports SocketJack.Serialization
Imports SocketJack
Imports System.Reflection
Imports System.Threading
Imports heirowDebug.WPF.Values
Imports heirowDebug.WPF.Collections
Imports heirowDebug.WPF.States

Public Class DebugWatcher
    Implements IDisposable

#Region "IDisposable"
    Private disposedValue As Boolean
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                _isEnabled = False
                DisposeWorkers()

                ' Remove from static dictionary to prevent memory leak
                SyncLock Watchers
                    If Watchers.ContainsKey(Guid) Then
                        Watchers.Remove(Guid)
                    End If
                End SyncLock

                ' Dispose and clear cached values after the sampler has been stopped.
                SyncLock ValueSync
                    For Each kvp In FieldValues
                        kvp.Value.Dispose()
                    Next
                    FieldValues.Clear()

                    For Each kvp In PropertyValues
                        kvp.Value.Dispose()
                    Next
                    PropertyValues.Clear()
                End SyncLock

                ChildWatcherValues.Clear()
                ChildWatcherGuids.Clear()

                _AttachedObject = Nothing
                _Parent = Nothing
            End If

            disposedValue = True
        End If
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub
#End Region

#Region "Caches"

    Private FieldValues As New SortedDictionary(Of Integer, DebugValue)
    Private PropertyValues As New SortedDictionary(Of Integer, DebugValue)
    Private ReadOnly ValueSync As New Object()
    Private NextMemberIndex As Integer

    Public ChildWatcherValues As New List(Of String)
    Public ChildWatcherGuids As New List(Of String)

#End Region

#Region "Properties"

    Public Property Guid As String = System.Guid.NewGuid().ToString()
    Public Property ValueCount As Integer = 0

    Public Shared Property FirstTimeEnabled As DateTime = Nothing

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

    ''' <summary>
    ''' The reflected type. For an instance watcher this is the attached object's
    ''' runtime type; for a static watcher it is the selected static-member owner.
    ''' </summary>
    Public ReadOnly Property TargetType As Type
        Get
            Return _TargetType
        End Get
    End Property
    Private _TargetType As Type

    Public ReadOnly Property IsStaticRoot As Boolean
        Get
            Return _IsStaticRoot
        End Get
    End Property
    Private _IsStaticRoot As Boolean

    Public Property IsRecursive As Boolean
        Get
            Return _Recursive
        End Get
        Set(value As Boolean)
            _Recursive = value
        End Set
    End Property
    Private _Recursive As Boolean = False

    Public Property Parent As DebugWatcher
        Get
            Return _Parent
        End Get
        Set(value As DebugWatcher)
            _Parent = value
        End Set
    End Property
    Private _Parent As DebugWatcher

    Public ReadOnly Property ParentValueGuid As String
        Get
            Return _ParentValueGuid
        End Get
    End Property
    Private _ParentValueGuid As String

    Public ReadOnly Property Depth As Integer
        Get
            Return _Depth
        End Get
    End Property
    Private _Depth As Integer = 0

    Public ReadOnly Property isChild As Boolean
        Get
            Return _isChild
        End Get
    End Property
    Private _isChild As Boolean = False

    Public Property isEnabled As Boolean
        Get
            Return _isEnabled
        End Get
        Set(value As Boolean)
            If _isEnabled = value Then Return
            Dim oldState As Boolean = isEnabled
            _isEnabled = value
            If isEnabled Then
                CreateWorkers()
            Else
                DisposeWorkers()
            End If
            Dim children As List(Of DebugWatcher)
            SyncLock Watchers
                children = ChildWatcherGuids.Where(Function(childGuid) Watchers.ContainsKey(childGuid)).
                    Select(Function(childGuid) Watchers(childGuid)).ToList()
            End SyncLock
            For Each child In children
                child.isEnabled = isEnabled
            Next
            If oldState <> isEnabled Then RaiseEvent EnabledStateChanged(isEnabled)
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


#Region "Shared"

    Public Shared Property EnableDispatcherProperties As Boolean = False
    Public Shared Property BlacklistedTypes As New List(Of Type)
    Public Shared Property Watchers As New Dictionary(Of String, DebugWatcher)
    Public Shared Property DebugWindow As DebugWindow
    Public Shared Property MaxRecursionDepth As Integer = 2
    Public Shared Property MaximumWatcherCount As Integer = 24
    Public Shared Property MaximumChildWatchersPerParent As Integer = 6
    Public Shared Property MaximumMembersPerWatcher As Integer = 192
    Public Shared Property MaximumMembersPerPass As Integer = 8

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

    ''' <summary>
    ''' Adds one top-level watcher for a live WPF window, reusing the existing
    ''' watcher when the same instance has already been added.
    ''' </summary>
    Public Shared Function WatchWindow(window As System.Windows.Window, Recursive As Boolean) As DebugWatcher
        If window Is Nothing Then Return Nothing

        SyncLock Watchers
            Dim existing = Watchers.Values.FirstOrDefault(
                Function(candidate) Not candidate.isChild AndAlso
                                    Not candidate.IsStaticRoot AndAlso
                                    Object.ReferenceEquals(candidate.AttachedObject, window))
            If existing IsNot Nothing Then Return existing
            If Watchers.Count >= MaximumWatcherCount Then Return Nothing

            Dim title = If(String.IsNullOrWhiteSpace(window.Title), "untitled", window.Title.Trim())
            Dim typeName = If(window.GetType().FullName, window.GetType().Name)
            Dim watcher As New DebugWatcher(window, Recursive, "[Window] " & typeName & " — " & title)
            If DebugWindow IsNot Nothing AndAlso DebugWindow.Enabled Then watcher.StartSession()
            Return watcher
        End SyncLock
    End Function

    ''' <summary>
    ''' Adds a top-level watcher for an object reached through a static member.
    ''' </summary>
    Public Shared Function WatchStaticObject(instance As Object, sourceName As String, Recursive As Boolean) As DebugWatcher
        If instance Is Nothing Then Return Nothing

        SyncLock Watchers
            Dim existing = Watchers.Values.FirstOrDefault(
                Function(candidate) Not candidate.isChild AndAlso
                                    Not candidate.IsStaticRoot AndAlso
                                    Object.ReferenceEquals(candidate.AttachedObject, instance))
            If existing IsNot Nothing Then Return existing
            If Watchers.Count >= MaximumWatcherCount Then Return Nothing

            Dim runtimeType = If(instance.GetType().FullName, instance.GetType().Name)
            Dim watcher As New DebugWatcher(instance, Recursive, "[Static object] " & sourceName & " ? " & runtimeType)
            If DebugWindow IsNot Nothing AndAlso DebugWindow.Enabled Then watcher.StartSession()
            Return watcher
        End SyncLock
    End Function

    ''' <summary>
    ''' Adds one top-level watcher whose values are the static fields and
    ''' properties exposed by the selected type.
    ''' </summary>
    Public Shared Function WatchStaticType(target As Type, Recursive As Boolean) As DebugWatcher
        If target Is Nothing OrElse target.ContainsGenericParameters Then Return Nothing

        SyncLock Watchers
            Dim existing = Watchers.Values.FirstOrDefault(
                Function(candidate) Not candidate.isChild AndAlso
                                    candidate.IsStaticRoot AndAlso
                                    candidate.TargetType Is target)
            If existing IsNot Nothing Then Return existing
            If Watchers.Count >= MaximumWatcherCount Then Return Nothing

            Dim watcher As New DebugWatcher(target, Recursive, True)
            If DebugWindow IsNot Nothing AndAlso DebugWindow.Enabled Then watcher.StartSession()
            Return watcher
        End SyncLock
    End Function

#End Region

#Region "Events"

    Public Event EnabledStateChanged(isEnabled As Boolean)

    ''' <summary>
    ''' Fires when the watcher has a new value.
    ''' </summary>
    ''' <param name="Watcher">The associated Watcher</param>
    ''' <param name="Value">The Value that was calculated/processed</param>
    Public Event ValueCalculated(Watcher As DebugWatcher, Value As DebugValue)

    ''' <summary>
    ''' Detects main & child Item Value Changes
    ''' </summary>
    ''' <param name="Watcher">The associated Watcher</param>
    ''' <param name="Value">The Value that changed</param>
    ''' <param name="ArrayIndexies">The Index(s) that have changed</param>
    Public Event ValueChanged(Watcher As DebugWatcher, Value As DebugValue, ArrayIndexies As List(Of Integer))

    Public Sub OnChangedValue(Watcher As DebugWatcher, ChangedValue As DebugValue)
        RaiseEvent ValueChanged(Watcher, ChangedValue, ChangedValue.ChangedIndexies)
    End Sub

    Public Sub OnValueCalculated(Watcher As DebugWatcher, Value As DebugValue)
        RaiseEvent ValueCalculated(Watcher, Value)
    End Sub

#End Region


#Region "Initializers"

    ''' <summary>
    ''' Create a new DebugWatcher
    ''' </summary>
    Public Sub New(AttachedTo As Object)
        Initialize(AttachedTo)
    End Sub

    ''' <summary>
    ''' Create a new DebugWatcher
    ''' </summary>
    ''' <param name="AttachedTo">The target object for debugging.</param>
    ''' <param name="Recursive">Recursive functionality disabled until finished.</param>
    Public Sub New(AttachedTo As Object, Recursive As Boolean)
        _Recursive = Recursive
        Initialize(AttachedTo)
    End Sub

    ''' <summary>
    ''' Creates an instance watcher with a debugger-friendly display name.
    ''' </summary>
    Public Sub New(AttachedTo As Object, Recursive As Boolean, Name As String)
        _Recursive = Recursive
        Initialize(AttachedTo, Name)
    End Sub

    Private Sub New(target As Type, Recursive As Boolean, staticRoot As Boolean)
        _Recursive = Recursive
        _IsStaticRoot = staticRoot
        _TargetType = target
        _Name = "[Static] " & If(target.FullName, target.Name)
        _AttachedObject = Nothing

        Dim flags = BindingFlags.Static Or BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.FlattenHierarchy
        Dim staticFields = target.GetFields(flags).
            Where(Function(info) Not info.IsLiteral AndAlso
                                  Not info.IsDefined(GetType(System.Runtime.CompilerServices.CompilerGeneratedAttribute), False)).
            ToArray()
        _Fields = staticFields.Take(MaximumMembersPerWatcher \ 2).
            Select(Function(info, index) New FieldReference(info, index)).ToArray()

        Dim staticProperties = target.GetProperties(flags).
            Where(Function(info) info.GetIndexParameters().Length = 0 AndAlso
                                 info.GetMethod IsNot Nothing AndAlso info.GetMethod.IsStatic).
            ToArray()
        _Properties = staticProperties.Take(Math.Max(0, MaximumMembersPerWatcher - _Fields.Length)).
            Select(Function(info, index) New PropertyReference(info, index)).ToArray()

        ValueCount = _Fields.Length + _Properties.Length
        SyncLock DebugWatcher.Watchers
            DebugWatcher.Watchers.Add(Guid, Me)
        End SyncLock
    End Sub

    ''' <summary>
    ''' Only for use internally.
    ''' </summary>
    ''' <param name="Parent"></param>
    ''' <param name="ParentValueGuid"></param>
    ''' <param name="AttachedTo"></param>
    ''' <param name="Recursive">Recursive functionality disabled until finished.</param>
    Public Sub New(Parent As DebugWatcher, ParentValueGuid As String, AttachedTo As Object, Recursive As Boolean)
        _Recursive = Recursive
        _Parent = Parent
        _isChild = True
        _ParentValueGuid = ParentValueGuid
        _Depth = Parent.Depth + 1
        Parent.ChildWatcherValues.Add(ParentValueGuid)
        Parent.ChildWatcherGuids.Add(Guid)
        Initialize(AttachedTo)
    End Sub

    Private Sub Initialize(AttachedTo As Object, Optional Name As String = "")
        If AttachedTo Is Nothing Then Throw New ArgumentNullException(NameOf(AttachedTo))
        _Name = If(Name = "", AttachedTo.GetType().ToString(), Name)
        _AttachedObject = AttachedTo
        _TargetType = AttachedTo.GetType()

        Dim flags = BindingFlags.Instance Or BindingFlags.[Public] Or BindingFlags.NonPublic
        _Fields = Wrapper.GetFieldReferences(_TargetType, flags).Take(MaximumMembersPerWatcher \ 2).ToArray()
        _Properties = Wrapper.GetPropertyReferences(_TargetType, flags).
            Take(Math.Max(0, MaximumMembersPerWatcher - _Fields.Length)).ToArray()

        ValueCount = If(Fields IsNot Nothing, _Fields.Count, 0) + If(Properties IsNot Nothing, _Properties.Count, 0)
        If NotNothing(Parent) Then
            isEnabled = Parent.isEnabled
        End If
        SyncLock DebugWatcher.Watchers
            DebugWatcher.Watchers.Add(Guid, Me)
        End SyncLock

    End Sub

    Public Shared Function CreateChild(Parent As DebugWatcher, ParentValueGuid As String, AttachedTo As Object, Recursive As Boolean) As DebugWatcher
        If Parent Is Nothing OrElse AttachedTo Is Nothing OrElse Parent.Depth >= MaxRecursionDepth Then Return Nothing
        SyncLock Watchers
            If Watchers.Count >= MaximumWatcherCount OrElse
               Parent.ChildWatcherValues.Count >= MaximumChildWatchersPerParent OrElse
               Parent.ChildWatcherValues.Contains(ParentValueGuid) Then Return Nothing

            Dim ancestor As DebugWatcher = Parent
            While ancestor IsNot Nothing
                If Object.ReferenceEquals(ancestor.AttachedObject, AttachedTo) Then Return Nothing
                ancestor = ancestor.Parent
            End While

            Return New DebugWatcher(Parent, ParentValueGuid, AttachedTo, Recursive)
        End SyncLock
    End Function

    Private Sub DisposeWorkers()
        If _workerCts IsNot Nothing Then
            _workerCts.Cancel()
            _workerCts.Dispose()
            _workerCts = Nothing
        End If
        Workers.Clear()
    End Sub

    Private Sub CreateWorkers()
        DisposeWorkers()
        _workerCts = New CancellationTokenSource()
        Dim token As CancellationToken = _workerCts.Token

        ' One sampler per watcher avoids an unbounded task/thread-pool fan-out for
        ' objects with hundreds of members.
        Workers.Add(Task.Run(Function() WorkerCalculateAsync(token)))
    End Sub

#End Region

#Region "Background Workers"

    Private Workers As New List(Of Task)
    Private _workerCts As CancellationTokenSource

    Private Shared Function IsThreadSafeType(t As Type) As Boolean
        Return Not GetType(System.Windows.Threading.DispatcherObject).IsAssignableFrom(t)
    End Function
    Dim lastfieldorpropname As String = ""
    Private Async Function WorkerCalculateAsync(token As CancellationToken) As Task
        Do While isEnabled AndAlso Not token.IsCancellationRequested
            Dim StartTime As DateTime = DateTime.UtcNow
            SampleNextMembers(StartTime, token)
            Dim ResultInterval As TimeSpan = DateTime.UtcNow - StartTime

            Try
                If ResultInterval.TotalMilliseconds < IntervalMilliseconds Then
                    Dim diff As Double = Math.Min(Math.Max(0, Interval.TotalMilliseconds - ResultInterval.TotalMilliseconds), Interval.TotalMilliseconds)
                    If diff > 0 Then Await Task.Delay(TimeSpan.FromMilliseconds(diff), token)
                Else
                    Await Task.Delay(MinimumInterval, token)
                End If
            Catch ex As OperationCanceledException
                Exit Do
            End Try
        Loop
    End Function

    Private Sub SampleNextMembers(started As DateTime, token As CancellationToken)
        Dim fieldCount = If(Fields Is Nothing, 0, Fields.Length)
        Dim propertyCount = If(Properties Is Nothing, 0, Properties.Length)
        Dim total = fieldCount + propertyCount
        If total = 0 Then Return

        Dim budget = Math.Min(total, Math.Max(1, MaximumMembersPerPass))
        For sampleIndex As Integer = 0 To budget - 1
            If token.IsCancellationRequested OrElse Not isEnabled Then Return
            Dim memberIndex = NextMemberIndex Mod total
            NextMemberIndex = (NextMemberIndex + 1) Mod total
            If memberIndex < fieldCount Then
                Dim field = Fields(memberIndex)
                If Not DebugValue.IgnoreTypes.Contains(field.Info.FieldType) AndAlso IsThreadSafeType(field.Info.FieldType) Then
                    ProcessSample(CurrentFieldValue(field), started)
                End If
            Else
                Dim prop = Properties(memberIndex - fieldCount)
                If Not DebugValue.IgnoreTypes.Contains(prop.Info.PropertyType) AndAlso IsThreadSafeType(prop.Info.PropertyType) Then
                    ProcessSample(CurrentPropertyValue(prop), started)
                End If
            End If
        Next
    End Sub

    Private Sub ProcessSample(v As DebugValue, started As DateTime)
        If v Is Nothing OrElse v.Guid Is Nothing Then Return
        Dim isInitial As Boolean
        Dim snapshot As DebugValue = Nothing
        SyncLock ValueTimeline.Timelines
            isInitial = Not ValueTimeline.Timelines.ContainsKey(v.Guid)
            If Not isInitial AndAlso Not v.ValueChanged Then Return
            snapshot = v.Clone()
            snapshot.CalculationTime = DateTime.UtcNow - started
            If isInitial Then
                ValueTimeline.Timelines.Add(snapshot.Guid, New ValueTimeline(snapshot.Guid, snapshot))
            Else
                ValueTimeline.Timelines(snapshot.Guid).AddValue(snapshot)
            End If
        End SyncLock
        OnValueCalculated(Me, snapshot)
        If snapshot.ValueChanged Then OnChangedValue(Me, snapshot)
    End Sub

#End Region

#Region "General Logic"

    Public Sub StartSession()
        If Not isEnabled Then isEnabled = True
    End Sub

    ''' <summary>
    ''' Publishes one complete snapshot to newly attached debugger UI handlers.
    ''' This keeps dynamically added roots visible even when their first sampler
    ''' pass completed before the DebugWindow subscribed to their events.
    ''' </summary>
    Public Sub PublishCurrentValues()
        Dim started = DateTime.UtcNow
        Dim cachedValues As List(Of DebugValue)
        SyncLock ValueSync
            cachedValues = FieldValues.Values.Concat(PropertyValues.Values).Where(Function(value) value IsNot Nothing).ToList()
        End SyncLock
        For Each value In cachedValues
            PublishCurrentValue(value, started)
        Next
    End Sub

    Private Sub PublishCurrentValue(value As DebugValue, started As DateTime)
        If value Is Nothing OrElse value.Guid Is Nothing Then Return
        Dim snapshot = value.Clone()
        snapshot.CalculationTime = DateTime.UtcNow - started
        OnValueCalculated(Me, snapshot)
        If snapshot.ValueChanged Then OnChangedValue(Me, snapshot)
    End Sub

    Public Sub EndSession()
        If isEnabled Then isEnabled = False
    End Sub

#End Region

    Public Function CurrentFieldValue(f As FieldReference) As DebugValue
        SyncLock ValueSync
            Try
                If FieldValues.ContainsKey(f.Index) Then
                    Dim StateObject As New ValueWorkerState(ReferenceType.Field) With {.Reference = f, .Instance = AttachedObject, .Watcher = Me}
                    Return FieldValues(f.Index).UpdateValue(StateObject)
                Else
                    Dim newValue As DebugValue = DebugValue.NewFieldValue(Me, f, AttachedObject, IsRecursive).SetValueChanged(True)
                    FieldValues.Add(f.Index, newValue)
                    SyncLock ValueTimeline.Timelines
                        If Not ValueTimeline.Timelines.ContainsKey(newValue.Guid) Then ValueTimeline.Timelines.Add(newValue.Guid, New ValueTimeline(newValue.Guid))
                    End SyncLock
                    Return newValue
                End If
            Catch ex As Exception
                Return Nothing
            End Try
        End SyncLock
    End Function

    Public Function CurrentPropertyValue(p As PropertyReference) As DebugValue
        SyncLock ValueSync
            Try
                If PropertyValues.ContainsKey(p.Index) Then
                    Dim StateObject As New ValueWorkerState(ReferenceType.Property) With {.Reference = p, .Instance = AttachedObject, .Watcher = Me}
                    Return PropertyValues(p.Index).UpdateValue(StateObject)
                Else
                    Dim newValue As DebugValue = DebugValue.NewPropertyValue(Me, p, AttachedObject, IsRecursive).SetValueChanged(True)
                    PropertyValues.Add(p.Index, newValue)
                    SyncLock ValueTimeline.Timelines
                        If Not ValueTimeline.Timelines.ContainsKey(newValue.Guid) Then ValueTimeline.Timelines.Add(newValue.Guid, New ValueTimeline(newValue.Guid))
                    End SyncLock
                    Return newValue
                End If
            Catch ex As Exception
                Return Nothing
            End Try
        End SyncLock
    End Function

End Class
