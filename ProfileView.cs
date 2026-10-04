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
    internal class ProfileView(Canvas canvas, Runway runway)
    {
        private double xscale = 1;
        private double yscale = 1;
        private Canvas Canvas = canvas;
        public Runway Runway = runway;
        private void CalculateScale()
        {
            xscale = (Canvas.ActualWidth - 50) / (Runway.Distance+Runway.LengthNM);
            yscale = (Canvas.ActualHeight - 50) / (((Runway.Distance+Runway.LengthNM) * Math.Tan((Runway.GlideSlope + 5) * double.Pi / 180)* 6076.11549));
        }
        /// <summary>
        /// Horizontal position on screen of a point at the given distance from the touchdown point.
        /// </summary>
        private double X(double distanceFromTouchdownNM)
        {
            return (Runway.LengthNM - Runway.TouchdownNM + distanceFromTouchdownNM) * xscale;
        }
        /// <summary>
        /// Vertical position on screen of a height in ft above the threshold elevation.
        /// </summary>
        private double Y(double heightFt)
        {
            return Canvas.ActualHeight - heightFt * yscale;
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
        /// <summary>
        /// Draws a line starting at the touchdown point with angle GlideSlope + angleOffset:
        /// dashed from touchdown to threshold, solid from threshold to the end of the display.
        /// </summary>
        private void AddGlidePathLine(double angleOffset, Brush stroke, double thickness)
        {
            double threshold = Runway.TouchdownNM;
            double end = Runway.TouchdownNM + Runway.Distance;
            AddLine(X(0), Y(0), X(threshold), Y(Runway.GlidePathHeight(threshold, angleOffset)), stroke, thickness, dashed: true);
            AddLine(X(threshold), Y(Runway.GlidePathHeight(threshold, angleOffset)), X(end), Y(Runway.GlidePathHeight(end, angleOffset)), stroke, thickness);
        }
        private void DrawAircraft(Aircraft aircraft)
        {
            double distance = aircraft.AlongTrackDistance(Runway);
            double distanceFromTouchdown = aircraft.DistanceFromTouchdown(Runway);
            double altitude = aircraft.Altitude;
            var color = aircraft.IsWithinGlidePathTolerance(Runway) ? Brushes.Green : Brushes.Red;
            Ellipse elipse = new()
            {
                Height = 10,
                Width = 10,
                Stroke = color,
                Fill = color
            };
            Canvas.SetBottom(elipse, (altitude - Runway.Elevation) * yscale - 5);
            Canvas.SetLeft(elipse, (distance + Runway.LengthNM) * xscale - 5);
            Canvas.Children.Add(elipse);
            TextBlock textBlock = new()
            {
                Text = $"{aircraft.Callsign}\n{altitude}\n{distanceFromTouchdown:0.0}NM",
                FontSize = 12,
                Foreground = Brushes.White
            };
            Canvas.SetBottom(textBlock, ((altitude - Runway.Elevation) * yscale + 15));
            Canvas.SetLeft(textBlock, ((distance + Runway.LengthNM) * xscale) - 15);
            Canvas.Children.Add(textBlock);
        }
        public void Draw(List<Aircraft> aircrafts)
        {
            CalculateScale();
            Line horizontal = new()
            {
                X1 = Runway.LengthNM*xscale,
                Y1 = Canvas.ActualHeight,
                X2 = (Runway.Distance+Runway.LengthNM) * xscale,
                Y2 = Canvas.ActualHeight,
                Stroke = Brushes.Green,
                StrokeThickness = 2
            };
            Canvas.Children.Add(horizontal);
            Line runwayh = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight,
                X2 = Runway.LengthNM * xscale,
                Y2 = Canvas.ActualHeight,
                Stroke = Brushes.Green,
                StrokeThickness = 3
            };
            Canvas.Children.Add(runwayh);
            Line runwayv = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight,
                X2 = Runway.LengthNM * xscale,
                Y2 = Canvas.ActualHeight - ((Runway.LengthNM * Math.Tan((Runway.GlideSlope + 5) * double.Pi / 180) * 6076.11549) * yscale),
                Stroke = Brushes.Green,
                StrokeThickness = 3
            };
            Canvas.Children.Add(runwayv);
            Line upper = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight,
                X2 = (Runway.LengthNM+Runway.Distance) * xscale,
                Y2 = Canvas.ActualHeight - (((Runway.LengthNM+Runway.Distance) * Math.Tan((Runway.GlideSlope + 5) * double.Pi / 180) * 6076.11549) * yscale),
                Stroke = Brushes.CadetBlue,
                StrokeThickness = 3
            };
            Canvas.Children.Add(upper);
            // Glide path and its tolerance limits all start at the touchdown point on the runway:
            // dashed between touchdown and threshold, solid beyond the threshold.
            AddGlidePathLine(0, Brushes.Yellow, 2);
            AddGlidePathLine(-Runway.GlidePathTolerance, Brushes.Red, 1);
            AddGlidePathLine(Runway.GlidePathTolerance, Brushes.Red, 1);
            // MDH line and missed approach point, where the glide path reaches the MDH.
            double mapt = Runway.MissedApproachPointNM;
            AddLine(0, Y(Runway.MDH), X(mapt), Y(Runway.MDH), Brushes.Red, 2);
            AddLine(X(mapt), Y(0), X(mapt), Y(Runway.MDH), Brushes.Red, 2);
            // Touchdown point: origin of the range marks and of the glide path.
            AddLine(X(0), Y(0), X(0), Y(0) - 12, Brushes.Yellow, 2);
            int num = 10;
            if(Runway.Distance == 15)
            {
                num = 15;
            }
            for (int i = 1; i <= num; i++)
            {
                SolidColorBrush stroke = Brushes.Green;
                // Range marks are measured from the touchdown point (not from the threshold),
                // as the distances given by the controller on final.
                double markX = Runway.LengthNM - Runway.TouchdownNM + i * Runway.Distance / num;
                Line distance = new()
                {
                    X1 = markX * xscale,
                    Y1 = Canvas.ActualHeight,
                    X2 = markX * xscale,
                    Y2 = Canvas.ActualHeight - ((markX * Math.Tan((Runway.GlideSlope+5) * double.Pi / 180) * 6076.11549) * yscale),
                    Stroke = stroke,
                    StrokeThickness = 1
                };
                Canvas.Children.Add(distance);
                TextBlock textBlock = new()
                {
                    Text = $"{i*Runway.Distance/num}NM",
                    FontSize = 12,
                    Foreground = Brushes.Yellow
                };
                Canvas.SetBottom(textBlock, 0);
                Canvas.SetLeft(textBlock, (markX * xscale-10));
                Canvas.Children.Add(textBlock);
            }
            foreach (Aircraft aircraft in aircrafts)
            {
                if (aircraft.IsDisplayed(runway))
                {
                    DrawAircraft(aircraft);
                }
            }
        }
    }
}
