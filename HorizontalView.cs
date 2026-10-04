using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    internal class HorizontalView(Canvas canvas, Runway runway)
    {
        private double xscale = 1;
        private double yscale = 1;
        Canvas Canvas = canvas;
        private Runway Runway = runway;
        private void CalculateScale()
        {
            xscale = (Canvas.ActualWidth - 50) / (Runway.Distance+Runway.LengthNM);
            yscale = (Canvas.ActualHeight - 50) / (Runway.Distance * Math.Tan(20*double.Pi/180));
        }
        public void Draw(List<Aircraft> aircrafts)
        {
            CalculateScale();
            Line runwayh = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight / 2,
                X2 = Runway.LengthNM * xscale,
                Y2 = Canvas.ActualHeight / 2,
                Stroke = Brushes.Green,
                StrokeThickness = 3
            };
            Canvas.Children.Add(runwayh);
            Line runwayv = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = (Canvas.ActualHeight / 2) - (Runway.WidthNM * yscale),
                X2 = Runway.LengthNM * xscale,
                Y2 = (Canvas.ActualHeight / 2) + (Runway.WidthNM * yscale),
                Stroke = Brushes.Green,
                StrokeThickness = 3
            };
            Canvas.Children.Add(runwayv);
            Line upper = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight / 2,
                X2 = (Runway.LengthNM + Runway.Distance) * xscale,
                Y2 = (Canvas.ActualHeight / 2) - ((Runway.Distance * Math.Tan((10) * double.Pi / 180)) * yscale),
                Stroke = Brushes.CadetBlue,
                StrokeThickness = 3
            };
            Canvas.Children.Add(upper);
            Line lower = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight / 2,
                X2 = (Runway.LengthNM + Runway.Distance) * xscale,
                Y2 = (Canvas.ActualHeight / 2) + ((Runway.Distance * Math.Tan((10) * double.Pi / 180)) * yscale),
                Stroke = Brushes.CadetBlue,
                StrokeThickness = 3
            };
            Canvas.Children.Add(lower);
            // Extended centreline and lateral tolerance limits all start at the touchdown point:
            // dashed between touchdown and threshold, solid beyond the threshold.
            double cy = Canvas.ActualHeight / 2;
            double xTouchdown = (Runway.LengthNM - Runway.TouchdownNM) * xscale;
            double xThreshold = Runway.LengthNM * xscale;
            double xEnd = (Runway.LengthNM + Runway.Distance) * xscale;
            double halfAtThreshold = Runway.CenterlineHalfWidth(Runway.TouchdownNM) * yscale;
            double halfAtEnd = Runway.CenterlineHalfWidth(Runway.TouchdownNM + Runway.Distance) * yscale;
            AddLine(xTouchdown, cy, xThreshold, cy, Brushes.Yellow, 2, dashed: true);
            AddLine(xThreshold, cy, xEnd, cy, Brushes.Yellow, 2);
            foreach (int side in new[] { -1, 1 })
            {
                AddLine(xTouchdown, cy, xThreshold, cy + side * halfAtThreshold, Brushes.Red, 1, dashed: true);
                AddLine(xThreshold, cy + side * halfAtThreshold, xEnd, cy + side * halfAtEnd, Brushes.Red, 1);
            }
            // Touchdown point: origin of the range marks and of the centreline tolerance.
            AddLine(xTouchdown, cy - 8, xTouchdown, cy + 8, Brushes.Yellow, 2);
            int num = 10;
            if (Runway.Distance == 15)
            {
                num = 15;
            }
            for (int i = 1; i <= num; i++)
            {
                SolidColorBrush stroke = Brushes.Green;
                // Range marks are measured from the touchdown point (not from the threshold).
                double x = (i * Runway.Distance / num - Runway.TouchdownNM + Runway.LengthNM) * xscale;

                Line distance = new()
                {
                    X1 = x,
                    Y1 = Canvas.ActualHeight / 2 + ((x / xscale) / (Runway.LengthNM + Runway.Distance) * Runway.Distance * Math.Tan(10 * Math.PI / 180) * yscale),
                    X2 = x,
                    Y2 = Canvas.ActualHeight / 2 - ((x / xscale) / (Runway.LengthNM + Runway.Distance) * Runway.Distance * Math.Tan(10 * Math.PI / 180) * yscale),
                    Stroke = stroke,
                    StrokeThickness = 1
                };

                Canvas.Children.Add(distance);
            }
            foreach (Aircraft aircraft in aircrafts)
            {
                if (aircraft.IsDisplayed(Runway))
                {
                    DrawAircraft(aircraft);
                }
            }
        }
        private void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness, bool dashed = false)
        {
            Line line = new()
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = stroke,
                StrokeThickness = thickness
            };
            if (dashed)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            Canvas.Children.Add(line);
        }
        private void DrawAircraft(Aircraft aircraft)
        {
            string callsign = aircraft.Callsign;
            double altitude = aircraft.Altitude;
            double distance = aircraft.AlongTrackDistance(Runway);
            double offset = aircraft.LateralOffset(Runway);
            var color = aircraft.IsWithinCenterlineTolerance(Runway) ? Brushes.Green : Brushes.Red;
            Ellipse elipse = new()
            {
                Height = 10,
                Width = 10,
                Stroke = color,
                Fill = color
            };
            double top = (Canvas.ActualHeight / 2) - offset * yscale - 5;
            if (top < 0) return;
            Canvas.SetTop(elipse, top);
            Canvas.SetLeft(elipse, (distance + Runway.LengthNM) * xscale - 5);
            Canvas.Children.Add(elipse);
            TextBlock textBlock = new()
            {
                Text = $"{callsign}\n{altitude}",
                FontSize = 12,
                Foreground = Brushes.White
            };
            Canvas.SetTop(textBlock, (Canvas.ActualHeight / 2) - (offset * yscale + 35) - 5);
            Canvas.SetLeft(textBlock, ((distance + Runway.LengthNM) * xscale) - 15);
            Canvas.Children.Add(textBlock);
        }
    }
}
