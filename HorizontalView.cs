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
            Line axis = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight / 2,
                X2 = (Runway.LengthNM + Runway.Distance) * xscale,
                Y2 = Canvas.ActualHeight / 2,
                Stroke = Brushes.Yellow,
                StrokeThickness = 2
            };
            Canvas.Children.Add(axis);
            Line axisP15 = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight / 2,
                X2 = (Runway.LengthNM + Runway.Distance) * xscale,
                Y2 = (Canvas.ActualHeight / 2) + (Runway.Distance * Math.Tan((1.5) * double.Pi / 180)*yscale),
                Stroke = Brushes.Red,
                StrokeThickness = 1
            };
            Canvas.Children.Add(axisP15);
            Line axisM15 = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight / 2,
                X2 = (Runway.LengthNM + Runway.Distance) * xscale,
                Y2 = (Canvas.ActualHeight / 2) - (Runway.Distance * Math.Tan((1.5) * double.Pi / 180) * yscale),
                Stroke = Brushes.Red,
                StrokeThickness = 1
            };
            Canvas.Children.Add(axisM15);
            int num = 10;
            if (Runway.Distance == 15)
            {
                num = 15;
            }
            for (int i = 1; i <= num; i++)
            {
                SolidColorBrush stroke = (i % 5 == 0) ? Brushes.Orange : Brushes.Green;
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
                    DrawAircraft(aircraft.Callsign, aircraft.Altitude, aircraft.AlongTrackDistance(runway), aircraft.LateralOffset(runway));
                }
            }
        }
        private void DrawAircraft(string callsign, double altitude, double distance, double offset)
        {
            var color = Brushes.White;
            if (Math.Abs(offset) <= distance * Math.Tan((1.5) * double.Pi / 180))
            {
                color = Brushes.Green;
            }
            else
            {
                color = Brushes.Red;
            }
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
