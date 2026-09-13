using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace QuiverLauncher.Services;

/// <summary>Owns theme palette generation and color selection dialogs.</summary>
public sealed class ThemeEditor
{
    private readonly Func<bool, Color> _getColor;
    private readonly Action<bool, Color> _applyColor;
    private readonly LauncherDialogLifetime _dialogs;

    public ThemeEditor(Func<bool, Color> getColor, Action<bool, Color> applyColor, LauncherDialogLifetime dialogs)
    {
        _getColor = getColor;
        _applyColor = applyColor;
        _dialogs = dialogs;
    }
        private bool IsLightColor(Color color)
        {
            // Calculate perceived brightness using standard formula
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return brightness > 0.5;
        }

        // Theme Color Shades
        private Color GetShadedColor(Color baseColor, double factor)
        {
            byte r = (byte)Math.Min(255, Math.Max(0, baseColor.R * factor));
            byte g = (byte)Math.Min(255, Math.Max(0, baseColor.G * factor));
            byte b = (byte)Math.Min(255, Math.Max(0, baseColor.B * factor));
            return Color.FromRgb(r, g, b);
        }

        public void ApplyResources(IResourceDictionary resources, Color primaryColor, Color secondaryColor)
        {
            var themeBase = new SolidColorBrush(primaryColor);
            var themeLighter = new SolidColorBrush(GetShadedColor(primaryColor, 1.3));
            var themeDarker = new SolidColorBrush(GetShadedColor(primaryColor, 0.7));
            var themeBorder = new SolidColorBrush(secondaryColor);

            var textColor = CalculateLuminance(primaryColor) > 0.5 ? Colors.Black : Colors.White;
            var tintedText = new SolidColorBrush(BlendColors(textColor, secondaryColor, 0.08));
            var tintedTextSecondary = new SolidColorBrush(
                CalculateLuminance(primaryColor) > 0.5
                    ? BlendColors(Color.FromRgb(70, 70, 70), secondaryColor, 0.15)
                    : BlendColors(Color.FromRgb(200, 200, 200), secondaryColor, 0.15)
            );

            resources["ThemeBase"] = themeBase;
            resources["ThemeLighter"] = themeLighter;
            resources["ThemeDarker"] = themeDarker;
            resources["ThemeBorder"] = themeBorder;
            resources["ThemeText"] = tintedText;
            resources["ThemeTextSecondary"] = tintedTextSecondary;
            resources["ThemeSuccess"] = new SolidColorBrush(Color.Parse("#22c55e"));
            resources["ThemeWarning"] = new SolidColorBrush(Color.Parse("#f59e0b"));
            resources["ThemeError"] = new SolidColorBrush(Color.Parse("#ef4444"));
            resources["ThemeAccent"] = new SolidColorBrush(Color.Parse("#f59e0b"));
            resources["ThemeFocusRing"] = new SolidColorBrush(Color.Parse("#38bdf8"));


        }

        private double CalculateLuminance(Color color)
        {
            return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        }

        // Color Picker Preset Dialog
        public async Task ShowPrimaryAsync()
        {
            // Simple color presets dialog
            var presets = new Dictionary<string, string>
            {
                { "Black", "#000000" },
                { "Darker Gray", "#101010" },
                { "Dark Gray (Default)", "#18181b" },
                { "Charcoal Gray", "#2c2c2c" },
                { "Slate Gray", "#36454f" },

                { "Dark Navy Blue", "#1e3a5f" },
                { "Deep Navy", "#0f2b46" },
                { "Deep Indigo", "#2c3e50" },
                { "Dark Grayish Blue", "#45475a" },
                { "Midnight Black", "#19191c" },

                { "Deep Forest Green", "#1a4d2e" },
                { "Darkest Green", "#063204" },
                { "Forest Green", "#228b22" },
                { "Deep Moss Green", "#2c5f2d" },
                { "Deep Forest", "#134411" },
                { "Black Forest Green", "#051D01" },
                { "Dark Olive Green", "#556b2f" },
                { "Dark Olive Drab", "#2a2922" },

                { "Deep Purple", "#2d1b4e" },
                { "Deep Plum", "#4b0082" },
                { "Dark Eggplant", "#614051" },

                { "Dark Burgundy", "#4d1f1f" },
                { "Burgundy", "#800020" },
                { "Deep Maroon", "#5c0b0b" },

                { "Light Gray", "#e5e5e5" },
                { "Silver Gray", "#c0c0c0" },
                { "Pale Gray", "#f0f0f0" },

                { "Soft Blue", "#d4e4f7" },
                { "Sky Blue", "#87ceeb" },
                { "Powder Blue", "#b0e0e6" },

                { "Seafoam Green", "#d4f1e8" },
                { "Mint Green", "#98fb98" },
                { "Bright Sea Foam", "#98ff98" }
            };

            await ShowColorPresetsDialog(presets);
        }

        // Secondary Color Picker
        public async Task ShowSecondaryAsync()
        {
            // Simple color presets dialog
            var presets = new Dictionary<string, string>
            {
                { "Black", "#000000" },
                { "Dark Gray (Default)", "#404040" },
                { "Gray", "#737373" },
                { "Light Gray", "#d4d4d4" },
                { "White", "#ffffff" },

                { "Red", "#ef4444" },
                { "Orange", "#f97316" },
                { "Yellow", "#eab308" },
                { "Lime", "#84cc16" },
                { "Green", "#10b981" },
                { "Teal", "#14b8a6" },
                { "Cyan", "#06b6d4" },
                { "Sky Blue", "#0ea5e9" },
                { "Blue", "#3b82f6" },
                { "Purple", "#a855f7" },
                { "Violet", "#8b5cf6" },
                { "Indigo", "#6366f1" },
                { "Pink", "#ec4899" }
            };

            await ShowColorPresetsDialog(presets, true);
        }

        private Color BlendColors(Color baseColor, Color blendColor, double blendAmount)
        {
            byte r = (byte)(baseColor.R * (1 - blendAmount) + blendColor.R * blendAmount);
            byte g = (byte)(baseColor.G * (1 - blendAmount) + blendColor.G * blendAmount);
            byte b = (byte)(baseColor.B * (1 - blendAmount) + blendColor.B * blendAmount);
            return Color.FromRgb(r, g, b);
        }

        private async Task ShowColorPresetsDialog(Dictionary<string, string> presets, bool isSecondary = false)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var stackPanel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

                    // Add Custom Color button at the top
                    var customButton = new Button
                    {
                        Content = "🎨 Custom Color Picker",
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Height = 50,
                        Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontWeight = FontWeight.Bold
                    };

                    customButton.Click += async (s, e) =>
                    {
                        // Close presets dialog
                        var window = TopLevel.GetTopLevel(s as Visual) as Window;
                        window?.Close();

                        // Open custom color picker
                        await ShowCustomColorPicker(isSecondary);
                    };

                    stackPanel.Children.Add(customButton);

                    // Add separator
                    stackPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Margin = new Thickness(0, 5, 0, 5)
                    });

                    foreach (var preset in presets)
                    {
                        var button = new Button
                        {
                            Content = preset.Key,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Height = 40,
                            Background = new SolidColorBrush(Color.Parse(preset.Value)),
                            Foreground = new SolidColorBrush(IsLightColor(Color.Parse(preset.Value)) ? Colors.Black : Colors.White),
                            Tag = preset.Value
                        };

                        button.Click += (s, e) =>
                        {
                            var colorHex = (s as Button)?.Tag as string;
                            if (!string.IsNullOrEmpty(colorHex))
                            {
                                _applyColor(isSecondary, Color.Parse(colorHex));

                                // Close the dialog after selection
                                if (s is Button btn && btn.Parent != null)
                                {
                                    var window = TopLevel.GetTopLevel(btn) as Window;
                                    window?.Close();
                                }
                            }
                        };

                        stackPanel.Children.Add(button);
                    }

                    var messageBox = new Window
                    {
                        Title = isSecondary ? "Select Secondary Color" : "Select Primary Color",
                        Width = 300,
                        Height = 1000,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new ScrollViewer { Content = stackPanel }
                    };
            _dialogs.Track(messageBox);

                    GamepadModalDialogNavigation.Attach(messageBox);

                    DesktopInterfaceScaling.PrepareDialog(messageBox, desktop.MainWindow);

                    await messageBox.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private async Task ShowCustomColorPicker(bool isSecondary = false)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var currentColor = _getColor(isSecondary);
                    var (h, s, l) = RgbToHsl(currentColor);

                    var pickerPanel = new StackPanel { Margin = new Thickness(20), Spacing = 15 };

                    // Preview box
                    var previewBorder = new Border
                    {
                        Width = 260,
                        Height = 60,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(currentColor),
                        BorderBrush = new SolidColorBrush(Colors.White),
                        BorderThickness = new Thickness(2)
                    };
                    pickerPanel.Children.Add(previewBorder);

                    // HSL Sliders
                    var hSlider = CreateHslSlider("Hue", h, 0, 360, "°");
                    var sSlider = CreateHslSlider("Saturation", s, 0, 100, "%");
                    var lSlider = CreateHslSlider("Lightness", l, 0, 100, "%");

                    pickerPanel.Children.Add(hSlider.panel);
                    pickerPanel.Children.Add(sSlider.panel);
                    pickerPanel.Children.Add(lSlider.panel);

                    // Update preview on slider change
                    EventHandler<AvaloniaPropertyChangedEventArgs> updatePreview = (s, e) =>
                    {
                        var newColor = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        previewBorder.Background = new SolidColorBrush(newColor);
                    };

                    hSlider.slider.PropertyChanged += updatePreview;
                    sSlider.slider.PropertyChanged += updatePreview;
                    lSlider.slider.PropertyChanged += updatePreview;

                    // Hex input
                    var hexPanel = new StackPanel { Spacing = 5 };
                    hexPanel.Children.Add(new TextBlock
                    {
                        Text = "Hex Color",
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    });

                    var hexBox = new TextBox
                    {
                        Text = $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}",
                        Watermark = "#RRGGBB",
                        Foreground = new SolidColorBrush(Colors.White),
                        Background = new SolidColorBrush(Color.FromRgb(40, 40, 40))
                    };

                    hexBox.TextChanged += (s, e) =>
                    {
                        try
                        {
                            var text = hexBox.Text?.Trim();
                            if (!string.IsNullOrEmpty(text) && text.StartsWith("#") && text.Length == 7)
                            {
                                var color = Color.Parse(text);
                                var (hue, sat, light) = RgbToHsl(color);
                                hSlider.slider.Value = hue;
                                sSlider.slider.Value = sat;
                                lSlider.slider.Value = light;
                            }
                        }
                        catch { }
                    };

                    // Update hex box when sliders change
                    EventHandler<AvaloniaPropertyChangedEventArgs> updateHex = (s, e) =>
                    {
                        var color = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        hexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    };

                    hSlider.slider.PropertyChanged += updateHex;
                    sSlider.slider.PropertyChanged += updateHex;
                    lSlider.slider.PropertyChanged += updateHex;

                    hexPanel.Children.Add(hexBox);
                    pickerPanel.Children.Add(hexPanel);

                    // Buttons
                    var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };

                    var applyButton = new Button
                    {
                        Content = "Apply",
                        Width = 120,
                        Height = 35,
                        Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                        Foreground = new SolidColorBrush(Colors.White)
                    };

                    var cancelButton = new Button
                    {
                        Content = "Cancel",
                        Width = 120,
                        Height = 35,
                        Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Foreground = new SolidColorBrush(Colors.White)
                    };

                    buttonPanel.Children.Add(applyButton);
                    buttonPanel.Children.Add(cancelButton);
                    pickerPanel.Children.Add(buttonPanel);

                    var pickerWindow = new Window
                    {
                        Title = isSecondary ? "Custom Secondary Color" : "Custom Primary Color",
                        Width = 320,
                        Height = 480,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                        Content = pickerPanel,
                        CanResize = false
                    };
            _dialogs.Track(pickerWindow);

                    applyButton.Click += (s, e) =>
                    {
                        var finalColor = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        var hexColor = $"#{finalColor.R:X2}{finalColor.G:X2}{finalColor.B:X2}";

                        _applyColor(isSecondary, finalColor);
                        pickerWindow.Close();
                    };

                    cancelButton.Click += (s, e) => pickerWindow.Close();

                    GamepadModalDialogNavigation.Attach(pickerWindow);

                    DesktopInterfaceScaling.PrepareDialog(pickerWindow, desktop.MainWindow);

                    await pickerWindow.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private (StackPanel panel, Slider slider) CreateHslSlider(string label, double initialValue, double min, double max, string unit)
        {
            var panel = new StackPanel { Spacing = 5 };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Width = 80
            });

            var valueText = new TextBlock
            {
                Text = $"{(int)initialValue}{unit}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Width = 50,
                TextAlignment = TextAlignment.Right
            };
            headerPanel.Children.Add(valueText);

            panel.Children.Add(headerPanel);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = initialValue,
                Width = 260,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };

            slider.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "Value")
                {
                    valueText.Text = $"{(int)slider.Value}{unit}";
                }
            };

            panel.Children.Add(slider);

            return (panel, slider);
        }

        // Convert RGB to HSL
        private (double h, double s, double l) RgbToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            double s = 0;
            double l = (max + min) / 2.0;

            if (delta != 0)
            {
                s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

                if (max == r)
                    h = ((g - b) / delta + (g < b ? 6 : 0)) / 6.0;
                else if (max == g)
                    h = ((b - r) / delta + 2) / 6.0;
                else
                    h = ((r - g) / delta + 4) / 6.0;
            }

            return (h * 360, s * 100, l * 100);
        }

        // Convert HSL to RGB
        private Color HslToRgb(double h, double s, double l)
        {
            h = h / 360.0;
            s = s / 100.0;
            l = l / 100.0;

            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;

                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return Color.FromRgb(
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255)
            );
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

}
