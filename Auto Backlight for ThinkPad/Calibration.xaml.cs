/*
 * Copyright 2019 Parth Patel
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Xml;
using UIElement = System.Windows.UIElement;

namespace Auto_Backlight_for_ThinkPad
{
    public struct DataPoint : IComparable<DataPoint>, IEquatable<DataPoint>
    {
        static DataPoint()
        {
            _dt = Assembly.Load("OxyPlot").CreateInstance("OxyPlot.DataPoint").GetType();
            Undefined = new DataPoint(Activator.CreateInstance(_dt, double.NaN, double.NaN));
        }
        public DataPoint(double x, double y) => _d = Activator.CreateInstance(_dt, x, y);
        public DataPoint(dynamic dp) => _d = dp;
        private static readonly Type _dt;
        private dynamic _d;

        public static readonly DataPoint Undefined;
        public double X { get => _d.X; }
        public double Y { get => _d.Y; }
        public bool IsDefined() => Equals(Undefined);
        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode();
        }
        public override bool Equals(object o)
        {
            if (o.GetType() != GetType()) return false;
            return Equals((DataPoint)o);
        }
        public bool Equals(DataPoint other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }
        public int CompareTo(DataPoint other)
        {
            int s = 0;
            if (s == 0) s = X.CompareTo(other.X);
            if (s == 0) s = Y.CompareTo(other.Y);
            return s;
        }
        public override string ToString()
        {
            return _d.ToString();
        }
        public static bool operator ==(DataPoint th, DataPoint other) => th.CompareTo(other) == 0;
        public static bool operator !=(DataPoint th, DataPoint other) => th.CompareTo(other) != 0;
        public static bool operator <(DataPoint th, DataPoint other) => th.CompareTo(other) < 0;
        public static bool operator <=(DataPoint th, DataPoint other) => th.CompareTo(other) <= 0;
        public static bool operator >(DataPoint th, DataPoint other) => th.CompareTo(other) > 0;
        public static bool operator >=(DataPoint th, DataPoint other) => th.CompareTo(other) >= 0;
        public static DataPoint operator +(DataPoint th, DataPoint other) => new DataPoint(th.X + other.X, th.Y + other.Y);
        public static DataPoint operator +(DataPoint th, double other) => new DataPoint(th.X + other, th.Y + other);
        public static DataPoint operator -(DataPoint th, DataPoint other) => new DataPoint(th.X - other.X, th.Y - other.Y);
        public static DataPoint operator -(DataPoint th, double other) => new DataPoint(th.X - other, th.Y - other);
        public static DataPoint operator *(DataPoint th, DataPoint other) => new DataPoint(th.X * other.X, th.Y * other.Y);
        public static DataPoint operator *(DataPoint th, double other) => new DataPoint(th.X * other, th.Y * other);
        public static DataPoint operator /(DataPoint th, DataPoint other) => new DataPoint(th.X / other.X, th.Y / other.Y);
        public static DataPoint operator /(DataPoint th, double other) => new DataPoint(th.X / other, th.Y / other);
        public static DataPoint operator %(DataPoint th, DataPoint other) => new DataPoint(th.X % other.X, th.Y % other.Y);
        public static DataPoint operator %(DataPoint th, double other) => new DataPoint(th.X % other, th.Y % other);
        public double Magnitude() => Math.Sqrt(Math.Pow(X, 2) + Math.Pow(Y, 2));
        public double Phase() => Math.Atan2(Y, X);
    }
}

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Window for viewing and editing the screen brightness level vs ambient light level calibration points
    /// </summary>
    public partial class Calibration : Window
    {
        /// <summary>
        /// Linear interpolate inside a set of points
        /// </summary>
        /// <param name="points">list of data-points</param>
        /// <param name="val">input value (X)</param>
        /// <returns>interpolated output value (Y)</returns>
        public static double Interpolate(List<DataPoint> points, double val)
        {
            // NaN for error
            if (double.IsNaN(val))
                return double.NaN;
            if (points.Count == 0)
                return double.NaN;
            else if (points.Count == 1)
                return points[0].Y;
            else
            {
                if (val <= points[0].X) return points[0].Y;
                if (val >= points[points.Count - 1].X) return points[points.Count - 1].Y;

                DataPoint p1 = points.Last(p => p.X <= val);
                DataPoint p2 = points.First(p => p.X > val);
                var slp = (p2.Y - p1.Y) / (p2.X - p1.X);
                var ret = ((val - p1.X) * slp) + p1.Y;
                return ret;
            }
        }
        /// <summary>
        /// Spline interpolation to generate smoothed curve
        /// </summary>
        /// <param name="inp">input points</param>
        /// <param name="outp">output points</param>
        /// <param name="tension">tension parameter, default</param>
        /// <returns>validity, false = line reverses (not a true function)</returns>
        public static bool CreateSmoothCurve(List<DataPoint> inp, List<DataPoint> outp, double tension = 0.5)
        {
            outp.Clear();
            var rangeX = (inp.Max(p => p.X) - inp.Min(p => p.X));
            var spline = new OxyPlot.CanonicalSpline(tension);
            if (inp.Count <= 2) outp.AddRange(inp);
            else outp.AddRange(spline.CreateSpline(inp.Select(p => new OxyPlot.DataPoint(p.X, p.Y)).ToList(), false, rangeX / 1000).Select(p => new DataPoint(p.X, p.Y)));
            bool valid = true;
            for (int i = 0; i < outp.Count - 1; i++)
            {
                DataPoint p1 = outp[i];
                DataPoint p2 = outp[i + 1];
                var dir = p2.X - p1.X;
                if (dir <= 0) valid = false;
            }
            return valid;
        }
        /// <summary>
        /// Raised when Apply/OK button is clicked to signal transfer of settings from this window
        /// </summary>
        public event EventHandler Apply;
        public class RefreshEventArgs : EventArgs
        {
            public List<DataPoint> LearnedPoints { get; }
            public double Curvature { get; }
            public RefreshEventArgs(List<DataPoint> learnedPoints, double curvature)
            {
                LearnedPoints = learnedPoints;
                Curvature = curvature;
            }
        }
        public delegate void RefreshEventHandler(object sender, RefreshEventArgs e);
        /// <summary>
        /// External refresh of the current point is requested
        /// </summary>
        public event RefreshEventHandler Refresh;
        /// <summary>
        /// Contans data for manual screen brightness change request
        /// </summary>
        public class ManualScreenBrightnessEventArgs : EventArgs
        {
            /// <summary>
            /// The requested screen brightness level (range 0 to 1)
            /// </summary>
            public double ScreenBrightness { get; }

            public ManualScreenBrightnessEventArgs(double screenBrightness)
            {
                ScreenBrightness = screenBrightness;
            }
        }
        /// <summary>
        /// Event handler type for screen brightness change request
        /// </summary>
        /// <param name="sender"><see cref="Calibration"/> instance</param>
        /// <param name="e">Contains screen brightness data</param>
        public delegate void ManualScreenBrightnessEventHandler(object sender, ManualScreenBrightnessEventArgs e);
        /// <summary>
        /// Manual user change of screen brightness is requested, the parameter is the brightness level (range 0 to 1)
        /// </summary>
        public event ManualScreenBrightnessEventHandler ManualScreenBrightness;
        /// <summary>
        /// Regenerate plot, including new smoothed curve
        /// </summary>
        /// <param name="newPoints">Optional new data-points to replace old</param>

        // Phase 28C.3: Native OxyPlot 2.2.0 model; no Contrib WPF wrappers.
        private OxyPlot.PlotModel _plotModel;
        private OxyPlot.Axes.LinearAxis _xAxis;
        private OxyPlot.Axes.LinearAxis _yAxis;
        private OxyPlot.Series.LineSeries _smoothedSeries;
        private OxyPlot.Series.LineSeries _scatterSeries;
        private OxyPlot.Annotations.LineAnnotation _currentLineX;
        private OxyPlot.Annotations.LineAnnotation _currentLineY;
        private OxyPlot.Annotations.TextAnnotation _invalidText;

        private void InitializeNativePlot()
        {
            _plotModel = new OxyPlot.PlotModel
            {
                Title = "Ambient light calibration curve",
                Background = OxyPlot.OxyColors.Transparent,
                TextColor = OxyPlot.OxyColors.White,
                PlotAreaBackground = OxyPlot.OxyColor.FromRgb(33, 33, 33),
                PlotAreaBorderColor = OxyPlot.OxyColors.White,
                PlotMargins = new OxyPlot.OxyThickness(50, 0, 50, 50)
            };
            _xAxis = new OxyPlot.Axes.LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Bottom,
                Title = "Ambient light (%)",
                Minimum = -0.1, Maximum = 100.1,
                IsPanEnabled = false, IsZoomEnabled = false,
                MajorGridlineStyle = OxyPlot.LineStyle.Solid,
                MinorGridlineStyle = OxyPlot.LineStyle.Dash,
                MajorGridlineColor = OxyPlot.OxyColors.Gray,
                MinorGridlineColor = OxyPlot.OxyColors.DimGray,
                AxislineColor = OxyPlot.OxyColors.White,
                TicklineColor = OxyPlot.OxyColors.White
            };
            _yAxis = new OxyPlot.Axes.LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Left,
                Title = "Screen brightness (%)",
                Minimum = -0.1, Maximum = 100.1,
                IsPanEnabled = false, IsZoomEnabled = false,
                MajorGridlineStyle = OxyPlot.LineStyle.Solid,
                MinorGridlineStyle = OxyPlot.LineStyle.Dash,
                MajorGridlineColor = OxyPlot.OxyColors.Gray,
                MinorGridlineColor = OxyPlot.OxyColors.DimGray,
                AxislineColor = OxyPlot.OxyColors.White,
                TicklineColor = OxyPlot.OxyColors.White
            };
            _plotModel.Axes.Add(_xAxis);
            _plotModel.Axes.Add(_yAxis);
            _smoothedSeries = new OxyPlot.Series.LineSeries
            {
                Title = "Smooth curve", RenderInLegend = false,
                CanTrackerInterpolatePoints = false,
                LineStyle = OxyPlot.LineStyle.Solid,
                Color = OxyPlot.OxyColors.White, StrokeThickness = 2
            };
            _scatterSeries = new OxyPlot.Series.LineSeries
            {
                Title = "Learn points", RenderInLegend = false,
                CanTrackerInterpolatePoints = false,
                LineStyle = OxyPlot.LineStyle.None,
                MarkerFill = OxyPlot.OxyColors.Yellow,
                MarkerSize = 5, MarkerType = OxyPlot.MarkerType.Circle
            };
            _plotModel.Series.Add(_smoothedSeries);
            _plotModel.Series.Add(_scatterSeries);
            _currentLineX = new OxyPlot.Annotations.LineAnnotation
            {
                Type = OxyPlot.Annotations.LineAnnotationType.Vertical,
                X = 50, LineStyle = OxyPlot.LineStyle.Dot,
                Color = OxyPlot.OxyColors.Yellow, StrokeThickness = 2
            };
            _currentLineY = new OxyPlot.Annotations.LineAnnotation
            {
                Type = OxyPlot.Annotations.LineAnnotationType.Horizontal,
                Y = 50, LineStyle = OxyPlot.LineStyle.Dot,
                Color = OxyPlot.OxyColors.Yellow, StrokeThickness = 2
            };
            _invalidText = new OxyPlot.Annotations.TextAnnotation
            {
                Text = "Invalid curvature",
                TextPosition = OxyPlot.DataPoint.Undefined,
                TextColor = OxyPlot.OxyColor.FromArgb(128, 255, 255, 255),
                Background = OxyPlot.OxyColor.FromArgb(128, 255, 0, 0),
                FontSize = 14
            };
            _plotModel.Annotations.Add(_currentLineX);
            _plotModel.Annotations.Add(_currentLineY);
            _plotModel.Annotations.Add(_invalidText);
            Plot.Model = _plotModel;
            // Disable default gestures; edits are routed through WPF pointer handlers.
            var controller = new OxyPlot.PlotController();
            controller.UnbindAll();
            Plot.Controller = controller;
        }

        private OxyPlot.ScreenPoint MouseScreenPoint()
        {
            var point = Mouse.GetPosition(Plot);
            return new OxyPlot.ScreenPoint(point.X, point.Y);
        }

        private DataPoint PlotDataPoint(OxyPlot.ScreenPoint pt)
        {
            double x = _xAxis.InverseTransform(pt.X);
            double y = _yAxis.InverseTransform(pt.Y);
            return new DataPoint(Math.Max(0, Math.Min(100, x)), Math.Max(0, Math.Min(100, y)));
        }

        private int HitLearnedPoint(OxyPlot.ScreenPoint pt)
        {
            var hit = _scatterSeries.HitTest(new OxyPlot.HitTestArguments(pt, _scatterSeries.MarkerSize));
            if (hit == null) return -1;
            int idx = (int)hit.Index;
            return idx >= 0 && idx < _learnedPoints.Count ? idx : -1;
        }

        public void RegenerateCurve()
        {
            // Regenerate smoothed
            bool valid = CreateSmoothCurve(_learnedPoints, _smoothedPoints, Curvature);
            if (valid)
            {
                _smoothedSeries.Color = OxyPlot.OxyColors.White;
                _invalidText.TextPosition = OxyPlot.DataPoint.Undefined;
            }
            else
            {
                _invalidText.TextPosition = new OxyPlot.DataPoint(50, 0);
                _smoothedSeries.Color = OxyPlot.OxyColors.Red;
            }

            _smoothedSeries.Points.Clear();
            foreach (var p in _smoothedPoints) _smoothedSeries.Points.Add(new OxyPlot.DataPoint(p.X, p.Y));
            _scatterSeries.Points.Clear();
            foreach (var p in _learnedPoints) _scatterSeries.Points.Add(new OxyPlot.DataPoint(p.X, p.Y));
            Plot.InvalidatePlot(true);
        }
        /// <summary>
        /// Get/set the current (intensity, brightness) point for the crosshair lines on the plot.
        /// Range 0 to 1, will be converted into 0 to 100 for display purposes.
        /// </summary>
        public DataPoint CurrentPoint
        {
            get => new DataPoint(_Current.X, _Current.Y) / 100.0;
            set
            {
                double X = value.X;
                double Y = value.Y;
                if (X < 0) X = 0;
                if (X > 1) X = 1;
                if (Y < 0) Y = 0;
                if (Y > 1) Y = 1;
                _Current = new DataPoint(X, Y) * 100.0;
            }
        }
        /// <summary>
        /// Get/set the learned data-points (unsmoothed).
        /// Range 0 to 1, will be converted into 0 to 100 for display purposes.
        /// </summary>
        public List<DataPoint> LearnedPoints
        {
            get => _LearnedPoints.Select(p => p / 100.0).ToList();
            set
            {
                _LearnedPoints = value.Select(p =>
                {
                    double X = p.X;
                    double Y = p.Y;
                    if (X < 0) X = 0;
                    if (X > 1) X = 1;
                    if (Y < 0) Y = 0;
                    if (Y > 1) Y = 1;
                    return new DataPoint(X, Y) * 100.0;
                }).ToList();
            }
        }
        public double Curvature
        {
            get => _Curvature / 100.0;
            set
            {
                if (value < 0) value = 0;
                if (value > 1) value = 1;
                _Curvature = value * 100.0;
            }
        }
        /// <summary>
        /// Create new window, optionally initialize with set of learned data-points
        /// </summary>
        /// <param name="learnedPoints">Optional list of learned data-points</param>
        public Calibration(List<DataPoint> learnedPoints = null, double curvature = 0.5)
        {
            InitializeComponent();
            InitializeNativePlot();
            if (learnedPoints != null) LearnedPoints = learnedPoints;
            Curvature = curvature;

            AddButton.Click += (sender, e) =>
            {
                var p = _Current;
                if (double.IsNaN(p.X) || double.IsNaN(p.Y)) return;
                _LearnedPoints.Add(p);
                _LearnedPoints = _LearnedPoints;
                AddButton.IsEnabled = false;
            };
            RefreshButton.Click += (sender, e) =>
            {
                Refresh?.Invoke(this, new RefreshEventArgs(LearnedPoints, Curvature));
            };
            CurvatureSlider.ValueChanged += (sender, e) => RegenerateCurve();
            ManualSlider.ValueChanged += (sender, e) =>
            {
                if (e.NewValue != _Current.Y)
                {
                    double br = e.NewValue / 100.0;
                    ManualScreenBrightness?.Invoke(this, new ManualScreenBrightnessEventArgs(br));
                }
            };

            // Bottom row buttons
            ApplyButton.Click += (sender, e) => Apply?.Invoke(this, EventArgs.Empty);
            CancelButton.Click += (sender, e) => Close();
            OkButton.Click += (sender, e) => { Apply?.Invoke(this, EventArgs.Empty); Close(); };

            // Phase 28C.3: handle edits with WPF pointer events (not obsolete
            // OxyPlot element events, which were attached to Contrib wrappers).
            int dragIdx = -1;
            Action queryCursor = () =>
            {
                var pt = MouseScreenPoint();
                bool inside = _plotModel.PlotArea.Contains(pt);
                if (!inside)
                {
                    Plot.Cursor = null;
                    return;
                }
                int idx = HitLearnedPoint(pt);
                bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                Plot.Cursor = dragIdx >= 0 ? Cursors.None :
                    ctrl ? (idx >= 0 ? Cursors.No : Cursors.UpArrow) :
                    (idx >= 0 ? Cursors.SizeAll : Cursors.Cross);
            };
            Plot.PreviewMouseLeftButtonDown += (sender, e) =>
            {
                var pt = MouseScreenPoint();
                if (!_plotModel.PlotArea.Contains(pt)) return;
                int idx = HitLearnedPoint(pt);
                bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                if (ctrl)
                {
                    if (idx >= 0) _LearnedPoints.RemoveAt(idx);
                    else _LearnedPoints.Add(PlotDataPoint(pt));
                    _LearnedPoints = _LearnedPoints;
                    e.Handled = true;
                }
                else if (idx >= 0)
                {
                    dragIdx = idx;
                    Plot.CaptureMouse();
                    e.Handled = true;
                }
                queryCursor();
            };
            Plot.PreviewMouseMove += (sender, e) =>
            {
                if (dragIdx >= 0 && e.LeftButton == MouseButtonState.Pressed)
                {
                    var value = PlotDataPoint(MouseScreenPoint());
                    // Keep distinct points separated by at least the marker radius.
                    double nearest = _LearnedPoints.Where((p, i) => i != dragIdx)
                        .Select(p => (p - value).Magnitude())
                        .DefaultIfEmpty(double.PositiveInfinity).Min();
                    if (nearest >= _scatterSeries.MarkerSize)
                    {
                        _LearnedPoints.RemoveAt(dragIdx);
                        _LearnedPoints.Add(value);
                        _LearnedPoints = _LearnedPoints;
                        dragIdx = _LearnedPoints.IndexOf(value);
                    }
                    e.Handled = true;
                }
                queryCursor();
            };
            Plot.PreviewMouseLeftButtonUp += (sender, e) =>
            {
                if (dragIdx < 0) return;
                dragIdx = -1;
                if (Plot.IsMouseCaptured) Plot.ReleaseMouseCapture();
                e.Handled = true;
                queryCursor();
            };
            Plot.LostMouseCapture += (sender, e) => { dragIdx = -1; };
            KeyDown += (sender, e) =>
            {
                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) queryCursor();
            };
            KeyUp += (sender, e) =>
            {
                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) queryCursor();
            };

            ContextMenu cm = (ContextMenu)Plot.FindResource("PlotMenu");
            Plot.PreviewMouseRightButtonUp += (sender, e) =>
            {
                var pos = e.GetPosition(Plot);
                var pt = new OxyPlot.ScreenPoint(pos.X, pos.Y);
                var inside = Plot.ActualModel.PlotArea.Contains(pt);
                if (inside)
                    Plot.ContextMenu = cm;
                else
                    Plot.ContextMenu = null;
            };
            cm.Opened += (sender, e) =>
            {
                // Check if selected a point
                var pos = Mouse.GetPosition(Plot);
                var pt = new OxyPlot.ScreenPoint(pos.X, pos.Y);
                double x = _xAxis.InverseTransform(pos.X);
                double y = _yAxis.InverseTransform(pos.Y);
                if (x < 0) x = 0;
                if (x > 100) x = 100;
                if (y < 0) y = 0;
                if (y > 100) y = 100;
                var value = new DataPoint(x, y);
                var hitPoint = _scatterSeries.HitTest(new OxyPlot.HitTestArguments(pt, _scatterSeries.MarkerSize));
                if (hitPoint != null) value = _LearnedPoints[(int)hitPoint.Index];

                // Enable based on whether point was selected
                cm.Items.OfType<MenuItem>().First(m => ((string)m.Header).StartsWith("Add")).IsEnabled = hitPoint == null;
                cm.Items.OfType<MenuItem>().First(m => ((string)m.Header).StartsWith("Add")).Tag = value;
                cm.Items.OfType<MenuItem>().First(m => ((string)m.Header).StartsWith("Remove")).IsEnabled = hitPoint != null;
                cm.Items.OfType<MenuItem>().First(m => ((string)m.Header).StartsWith("Remove")).Tag = value;
                cm.Items.OfType<MenuItem>().First(m => ((string)m.Header).StartsWith("Edit")).IsEnabled = hitPoint != null;
                cm.Items.OfType<MenuItem>().First(m => ((string)m.Header).StartsWith("Edit")).Tag = value;
            };

            RegenerateCurve();
        }
        private void _MenuAddPoint(object sender, RoutedEventArgs e)
        {
            MenuItem mi = (MenuItem)sender;
            _LearnedPoints.Add((DataPoint)mi.Tag);
            _LearnedPoints = _LearnedPoints;
        }
        private void _MenuRemovePoint(object sender, RoutedEventArgs e)
        {
            MenuItem mi = (MenuItem)sender;
            _LearnedPoints.Remove((DataPoint)mi.Tag);
            _LearnedPoints = _LearnedPoints;
        }
        private void _MenuEditPoint(object sender, RoutedEventArgs e)
        {
            MenuItem mi = (MenuItem)sender;
            var point = (DataPoint)mi.Tag;

            Func<UIElement, UIElement> CloneElement = (orig) =>
            {
                string s = XamlWriter.Save(orig);
                StringReader stringReader = new StringReader(s);
                XmlReader xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings());
                return (UIElement)XamlReader.Load(xmlReader);
            };
            var xyd = (Window)CloneElement((Window)Plot.FindResource("XYDialog"));
            TextBox xb = ((Grid)xyd.Content).Children.OfType<TextBox>().First();
            TextBox yb = ((Grid)xyd.Content).Children.OfType<TextBox>().Last();
            Button okb = ((Grid)xyd.Content).Children.OfType<StackPanel>().First().Children.OfType<Button>().First();
            Button cancelb = ((Grid)xyd.Content).Children.OfType<StackPanel>().First().Children.OfType<Button>().Last();
            var rounder = new Rounder();
            TextChangedEventHandler textHandler = (se, ev) =>
            {
                TextBox tb = (TextBox)se;
                e.Handled = true;
                double val = (double)rounder.ConvertBack(tb.Text.Trim(), typeof(double));
                if (!double.IsNaN(val)) tb.Tag = val;
                var c = tb.CaretIndex;
                tb.Text = (string)rounder.Convert(tb.Tag, typeof(string));
                tb.CaretIndex = c;
            };
            xb.Tag = point.X;
            xb.Text = (string)rounder.Convert(point.X, typeof(string));
            yb.Tag = point.Y;
            yb.Text = (string)rounder.Convert(point.Y, typeof(string));
            xb.TextChanged += textHandler;
            yb.TextChanged += textHandler;
            okb.Click += (se, ev) =>
            {
                xyd.Tag = new DataPoint((double)xb.Tag, (double)yb.Tag);
                xyd.Close();
            };
            cancelb.Click += (se, ev) => xyd.Close();
            xyd.ShowDialog();
            _LearnedPoints[_LearnedPoints.IndexOf(point)] = (DataPoint)xyd.Tag;
            _LearnedPoints = _LearnedPoints;
        }
        private DataPoint _Current
        {
            get => new DataPoint(_currentLineX.X, _currentLineY.Y);
            set
            {
                _currentLineX.X = value.X;
                _currentLineY.Y = value.Y;
                CurrentXText.Text = string.Format("({0:F0}%, ", value.X);
                CurrentYText.Text = string.Format("{0:F0}%)", value.Y);
                ManualSlider.Value = value.Y;

                // Distance to nearest other point must be large, avoids overlapping points
                double minDist = _LearnedPoints.Min(p => (p - value).Magnitude());
                AddButton.IsEnabled = minDist > _scatterSeries.MarkerSize;
                Plot.InvalidatePlot(true);
            }
        }
        private double _Curvature
        {
            get => CurvatureSlider.Value;
            set => CurvatureSlider.Value = value;
        }
        private List<DataPoint> _LearnedPoints
        {
            get => _learnedPoints;
            set
            {
                value = value.Distinct().ToList();
                value.Sort();
                _learnedPoints.Clear();
                _learnedPoints.AddRange(value);
                RegenerateCurve();
            }
        }
        private List<DataPoint> _learnedPoints = new List<DataPoint>();
        private List<DataPoint> _smoothedPoints = new List<DataPoint>();
    }
}