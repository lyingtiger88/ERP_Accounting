class JalaliDate {
  const JalaliDate({
    required this.year,
    required this.month,
    required this.day,
  });

  final int year;
  final int month;
  final int day;

  factory JalaliDate.fromGregorian(DateTime date) {
    const monthDays = <int>[
      0,
      31,
      59,
      90,
      120,
      151,
      181,
      212,
      243,
      273,
      304,
      334,
    ];

    final gy = date.year;
    final gm = date.month;
    final gd = date.day;
    final gy2 = gm > 2 ? gy + 1 : gy;

    var days = 355666 +
        (365 * gy) +
        ((gy2 + 3) ~/ 4) -
        ((gy2 + 99) ~/ 100) +
        ((gy2 + 399) ~/ 400) +
        gd +
        monthDays[gm - 1];

    var jy = -1595 + (33 * (days ~/ 12053));
    days %= 12053;

    jy += 4 * (days ~/ 1461);
    days %= 1461;

    if (days > 365) {
      jy += (days - 1) ~/ 365;
      days = (days - 1) % 365;
    }

    final int jm;
    final int jd;

    if (days < 186) {
      jm = 1 + (days ~/ 31);
      jd = 1 + (days % 31);
    } else {
      jm = 7 + ((days - 186) ~/ 30);
      jd = 1 + ((days - 186) % 30);
    }

    return JalaliDate(year: jy, month: jm, day: jd);
  }

  static DateTime toGregorian(
    int jalaliYear,
    int jalaliMonth,
    int jalaliDay,
  ) {
    var jy = jalaliYear + 1595;
    var days = -355668 +
        (365 * jy) +
        ((jy ~/ 33) * 8) +
        (((jy % 33) + 3) ~/ 4) +
        jalaliDay +
        (jalaliMonth < 7
            ? (jalaliMonth - 1) * 31
            : ((jalaliMonth - 7) * 30) + 186);

    var gy = 400 * (days ~/ 146097);
    days %= 146097;

    if (days > 36524) {
      days--;
      gy += 100 * (days ~/ 36524);
      days %= 36524;

      if (days >= 365) {
        days++;
      }
    }

    gy += 4 * (days ~/ 1461);
    days %= 1461;

    if (days > 365) {
      gy += (days - 1) ~/ 365;
      days = (days - 1) % 365;
    }

    var gd = days + 1;

    final leap =
        (gy % 4 == 0 && gy % 100 != 0) || (gy % 400 == 0);
    final monthLengths = <int>[
      0,
      31,
      leap ? 29 : 28,
      31,
      30,
      31,
      30,
      31,
      31,
      30,
      31,
      30,
      31,
    ];

    var gm = 1;

    while (gm <= 12 && gd > monthLengths[gm]) {
      gd -= monthLengths[gm];
      gm++;
    }

    return DateTime(gy, gm, gd);
  }

  String get formatted {
    return year.toString().padLeft(4, '0') +
        '/' +
        month.toString().padLeft(2, '0') +
        '/' +
        day.toString().padLeft(2, '0');
  }
}
