Namespace Values
    Public Class TypeFlags

        Public Property isArray As Boolean

        Public Property isSystem As Boolean

        Public Property isList As Boolean

        Public Property isDictionary As Boolean

        Public Property isNumeric As Boolean

        Public Property isBoolean As Boolean

        Public Property isIgnored As Boolean

        Public Property isXAML As Boolean

        Public Property isNothing As Boolean

        Public Function isGraphable() As Boolean
            Return isNumeric Or isBoolean AndAlso Not isNothing
        End Function

    End Class
End Namespace