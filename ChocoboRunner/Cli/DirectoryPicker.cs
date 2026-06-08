using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ChocoboRunner.Cli;

public static class DirectoryPicker
{
    public static string Picker()
    {
        IApplication app = Application.Create();
        app.Init();

        while (true)
        {
            Button newFolderButton = new Button
            {
                Text = "Create folder"
            };
            
            //Because I am new to this library I am using a default FileDialog
            //And have done a minimal version. Technically I should make one myself
            //so I have more control on where the button ends (unless I can order it still not looked into that)
            //And the context menu new works differently then my buttons implementation for making a directory
            //Theirs doesn't allow you to make deeper directories in one go. Instead of only blocking path traversal
            //with ..
            using FileDialog dialog = new()
            {
                AllowsMultipleSelection = false,
                OpenMode = OpenMode.Directory,
                Path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                MustExist =  true,
                Buttons = [newFolderButton]
            };
            
            newFolderButton.Accepting += (_, e) =>
            {
                string currentPath = dialog.Path;
                e.Handled = true;
                ShowCreateFolderDialog(app, dialog);
                
                //ugly but it works...
                dialog.Path = $"{currentPath}/";
                dialog.Path = $"{currentPath}";
            };

            app.Run(dialog);

            if (dialog.Canceled)
            {
                int? resulta = MessageBox.Query(app,
                    "You have not selected a directory.",
                    $"Do you want to close the application?", "Yes", "No");
                app.Dispose();
                if(resulta is 0) Environment.Exit(0); 
            }
            else
            {
                string selectedPath = dialog.Path;

                int? result = MessageBox.Query(app,
                    "Confirm Directory",
                    $"Is this the correct directory?\n\n{selectedPath}", "Yes", "No");

                if (result is not 0) continue; // Yes
                app.Dispose();
                Logger.Info($@"Chosen path to install to: {selectedPath}");
                return selectedPath;
            }
        }
    }
    
    private static void ShowCreateFolderDialog(
        IApplication app,
        FileDialog fileDialog)
    {
        TextField nameField = new TextField()
        {
            Title = "",
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1
        };

        Button okButton = new Button
        {
            Text = "OK",
            X = 1,
            Y = 3,
            IsDefault = true
        };

        Button cancelButton = new Button
        {
            Text = "Cancel",
            X = Pos.Right(okButton) + 2,
            Y = 3
        };

        Dialog popup = new Dialog
        {
            Title = "Create Folder",
            Width = 60,
            Height = 8
        };

        popup.Add(nameField);
        popup.Add(okButton);
        popup.Add(cancelButton);

        cancelButton.Accepting += (_, _) =>
        {
            app.RequestStop(popup);
        };

        okButton.Accepting += (_, _) =>
        {
            string? input = nameField.Text?.ToString()?.Trim();

            if (!TryCreateFolder(fileDialog.Path, input, out string error))
            {
                MessageBox.ErrorQuery(app, "Invalid Folder", error, "OK");

                return;
            }

            app.RequestStop(popup);
        };

        app.Run(popup);
    }
    
    private static bool TryCreateFolder(string basePath, string? relativePath, out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "Folder name cannot be empty.";
            return false;
        }

        if (Path.IsPathRooted(relativePath))
        {
            error = "Absolute paths are not allowed.";
            return false;
        }

        string combined =
            Path.Combine(basePath, relativePath);

        string fullBase =
            Path.GetFullPath(basePath);

        string fullTarget =
            Path.GetFullPath(combined);

        if (!fullTarget.StartsWith(fullBase, StringComparison.Ordinal))
        {
            error =
                "Path traversal is not allowed.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(fullTarget);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}