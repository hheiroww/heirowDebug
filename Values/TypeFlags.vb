Namespace Values
    Public Class TypeFlags
        Public Sub New() : End Sub

        Public Sub New(Optional Empty As Boolean = False)
            If Empty Then SetEmpty()
        End Sub

        Public Property isArray As Boolean

        Public Property isSystem As Boolean

        Public Property CalculatingChildren As Boolean

        Public Property isList As Boolean

        Public Property isDictionary As Boolean

        Public Property isNumeric As Boolean

        Public Property isBoolean As Boolean

        Public Property isDrawingPoint As Boolean

        Public Property isWindowsPoint As Boolean

        Public Property isDrawingRectangle As Boolean

        Public Property isShapesRect As Boolean

        Public Property isIgnored As Boolean

        Public Property isXAML As Boolean

        Public Property isNothing As Boolean

        Public Function SetCalcChildren(value As Boolean)
            CalculatingChildren = value
            Return Me
        End Function

        Public Sub SetEmpty()
            isArray = False
            isList = False
            isDictionary = False
            isSystem = False
            isBoolean = False
            isNumeric = False
            isIgnored = False
            isDrawingPoint = False
            isWindowsPoint = False
            isDrawingRectangle = False
            isShapesRect = False
            isNothing = True
        End Sub

        Public Function isGraphable() As Boolean
            Return (isNumeric Or
                    isBoolean Or
                    isDrawingPoint Or
                    isShapesRect Or
                    isDrawingRectangle Or
                    isDrawingRectangle) AndAlso
                    Not isNothing
        End Function

    End Class
End Namespace