import 'package:flutter_test/flutter_test.dart';
import 'package:erp_accounting/src/core/mobile/mobile_mode.dart';

void main() {
  test('standalone stays offline and does not activate sync', () {
    const config = MobileConfiguration(mode: MobileMode.standalone);
    expect(config.canSync, isFalse);
    expect(MobileConfiguration.fromJson(config.toJson()).mode, MobileMode.standalone);
  });

  test('companion requires pairing and secured endpoint', () {
    const pending = MobileConfiguration(
      mode: MobileMode.companion, serverUrl: 'http://localhost:5000',
      companyId: 'company', deviceId: 'mobile', paired: true,
    );
    expect(pending.canSync, isFalse);
    const paired = MobileConfiguration(
      mode: MobileMode.companion, serverUrl: 'https://erp.example.test',
      companyId: 'company', deviceId: 'mobile', paired: true,
    );
    expect(paired.canSync, isTrue);
    expect(MobileConfiguration.fromJson(paired.toJson()).canSync, isTrue);
  });
}
