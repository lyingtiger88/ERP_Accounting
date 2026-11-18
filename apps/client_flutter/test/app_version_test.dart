import 'package:flutter_test/flutter_test.dart';

import '../lib/src/core/app_version.dart';

void main() {
  test('Zarr version is shared by Windows and Android', () {
    expect(ZarrAppVersion.name, '0.5.0-beta.5');
    expect(ZarrAppVersion.build, 9);
    expect(ZarrAppVersion.display, '0.5.0-beta.5 (build 9)');
    expect(ZarrAppVersion.platforms, 'Windows • Android');
  });
}
