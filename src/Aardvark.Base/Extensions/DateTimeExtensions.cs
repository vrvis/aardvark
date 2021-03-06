﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Aardvark.Base
{
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Calculates the UTC Julian day from the given DateTime and time zone.
        /// Julian days start at 12h noon January 1, 4713 BC (day 0). The fractional part contains the fraction of a day since the last noon. 
        /// </summary>
        public static double ComputeJulianDayUTC(this DateTime date, double timeZone)
        {
            return date.ComputeJulianDay() - (timeZone / 24.0);
        }

        /// <summary>
        /// Calculates the Julian day from the given DateTime.
        /// Julian days start at 12h noon January 1, 4713 BC (day 0). The fractional part contains the fraction of a day since the last noon. 
        /// </summary>
        public static double ComputeJulianDay(this DateTime date)
        {
            var y = date.Year;
            var m = date.Month;
            var d = date.Day;
            var dt = date.TimeOfDay.TotalDays;

            if (m < 3)
            {
                m = m + 12;
                y = y - 1;
            }

            int jd = d + (153 * m - 457) / 5 + 365 * y + (y / 4) - (y / 100) + (y / 400);

            return jd + 1721118.5 + dt;
        }

        // whatever the use of this should be
        public static double GregorianDayOfMonthFromJulianDay(double J)
        {
            // the following calculates the day of the month of a JD
            var p = Fun.Floor(J + 0.5);
            var s1 = p + 68569;
            var n = Fun.Floor(4 * s1 / 146097);
            var s2 = s1 - Fun.Floor((146097 * n + 3) / 4);
            var i = Fun.Floor(4000 * (s2 + 1) / 1461001);
            var s3 = s2 - Fun.Floor(1461 * i / 4) + 31;
            var q = Fun.Floor(80 * s3 / 2447);
            var e = s3 - Fun.Floor(2447 * q / 80);
            // int s4 = System.Math.Floor(q / 11);
            // int m = q + 2 - 12 * s4;
            // int y = 100 * (n - 49) + i + s4;
            return e + J - p + 0.5;
        }
        
        /// <summary>
        /// Calculates date of a Julian day.
        /// NOTE: A Julian day starts at 12h noon
        /// </summary>
        public static DateTime ComputeDateFromJulianDay(double jd)
        {
            var p = (int)(jd + 68569.5);
            var q = 4 * p / 146097;
            var r = p - (146097 * q + 3) / 4;
            var s = 4000 * (r + 1) / 1461001;
            var t = r - 1461 * s / 4 + 31;
            var u = 80 * t / 2447;
            var v = u / 11;

            var Y = 100 * (q - 49) + s + v;
            var M = u + 2 - 12 * v;
            var D = t - 2447 * u / 80;

            var timeOfDay = (jd + 0.5).Frac();
            var tmp = timeOfDay * 24;
            var _h = (int)tmp;
            tmp = (tmp - _h) * 60;
            var _m = (int)tmp;
            tmp = (tmp - _m) * 60;
            var _s = (int)tmp;
            tmp = (tmp - _s) * 1000;
            var _ms = (int)tmp;
            
            return new DateTime(Y, M, D, _h, _m, _s, _ms);
        }

        /// <summary>
        /// Calculates date of a Julian day.
        /// NOTE: A Julian day starts at 12h noon
        /// </summary>
        public static DateTime ComputeDateFromJulianDay(int jd)
        {
            var p = (int)(jd + 68569);
            var q = 4 * p / 146097;
            var r = p - (146097 * q + 3) / 4;
            var s = 4000 * (r + 1) / 1461001;
            var t = r - 1461 * s / 4 + 31;
            var u = 80 * t / 2447;
            var v = u / 11;

            var Y = 100 * (q - 49) + s + v;
            var M = u + 2 - 12 * v;
            var D = t - 2447 * u / 80;

            return new DateTime(Y, M, D, 12, 0, 0, 0);
        }
    }
}
