# JackDebug.WPF
`JackDebug.WPF` is a object value visualizer you can use to hone-in on what your code is doing at **runtime**.

## Setup is **Simple** when installing with NuGet.
https://www.nuget.org/packages/JackDebug.WPF
```bash
 NuGet\Install-Package MicroSerializationLibrary -Version 1.0.2
```



### C#
```cs
    var dw = new DebugWatcher(Game, true);
    dw.StartSession();
```

### VB.NET
```vb
    Dim dw As New DebugWatcher(Game, True)
    dw.StartSession()
```


## Show the debug window.
### C#
```cs
    DebugWatcher.CreateDebugWindow();
```

### VB.NET
```vb
    DebugWatcher.CreateDebugWindow()
```

# Thats it!
![Demo](https://raw.githubusercontent.com/JackOfFates/Jack-Debug/refs/heads/main/demo1100.gif)

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

## License

[MIT](https://choosealicense.com/licenses/mit/)
