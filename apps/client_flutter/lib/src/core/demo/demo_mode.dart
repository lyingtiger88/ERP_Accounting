class DemoMode {
  const DemoMode._();

  static const companyId =
      '00000000-0000-4000-8000-000000000501';
  static const userId =
      '00000000-0000-4000-8000-000000000502';
  static const accessToken = '__ERP_LOCAL_DEMO__';
  static const displayName = 'کاربر تست آفلاین';
  static const role = 'Demo Administrator';
  static const documentLimit = 500;

  static bool isDemoToken(String? token) => token == accessToken;

  static bool isDemoCompany(String? id) => id == companyId;
}
