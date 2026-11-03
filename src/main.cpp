#include <Arduino.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

#define MIC_PIN 34
#define SERVICE_UUID        "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
#define CHARACTERISTIC_UUID "beb5483e-36e1-4688-b7f5-ea07361b26a8"

int dcOffset = 1840;
BLECharacteristic *pCharacteristic;
bool deviceConnected = false;

class MyServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
      deviceConnected = true;
    };
    void onDisconnect(BLEServer* pServer) {
      deviceConnected = false;
      // Перезапускаем рекламу, чтобы можно было подключиться снова
      pServer->getAdvertising()->start();
    }
};

void setup() {
  Serial.begin(115200);
  
  // 1. Инициализация BLE
  BLEDevice::init("SoundVisualizer");
  BLEServer *pServer = BLEDevice::createServer();
  pServer->setCallbacks(new MyServerCallbacks());

  BLEService *pService = pServer->createService(SERVICE_UUID);
  pCharacteristic = pService->createCharacteristic(
                      CHARACTERISTIC_UUID,
                      BLECharacteristic::PROPERTY_NOTIFY
                    );
  pCharacteristic->addDescriptor(new BLE2902()); // Важно для Notify!
  pService->start();

  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
  pAdvertising->addServiceUUID(SERVICE_UUID);
  pAdvertising->setScanResponse(true);
  pAdvertising->start();

  // 2. Калибровка (как в вашем старом коде)
  analogReadResolution(12);
  analogSetAttenuation(ADC_11db);
  
  Serial.println("Калибровка...");
  long sum = 0;
  for (int i = 0; i < 500; i++) {
    sum += analogRead(MIC_PIN);
    delay(2);
  }
  dcOffset = sum / 500;
  Serial.printf("Смещение: %d\n", dcOffset);
}

void loop() {
  if (!deviceConnected) {
    delay(100);
    return;
  }

  String dataStr = "AUD:";
  for (int i = 0; i < 10; i++) {
    int rawMic = analogRead(MIC_PIN);
    int val = rawMic - dcOffset;
    if (val > 1500) val = 1500;
    if (val < -1500) val = -1500;
    
    dataStr += String(val);
    if (i < 9) dataStr += ",";
  }

  // ВАЖНО: добавляем перенос строки, чтобы C# мог понять, где конец пакета
  dataStr += "\r\n"; 

  pCharacteristic->setValue(dataStr.c_str());
  pCharacteristic->notify();

  delay(5); 
}