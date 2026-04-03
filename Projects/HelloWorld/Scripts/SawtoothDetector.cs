// Sawtooth Detector — скрипт для Rapid SCADA v6
// Детектор пилообразного сигнала.
//
// Назначение:
//   Формула ставится на ОТСЛЕЖИВАЕМЫЙ канал (температура).
//   Скрипт анализирует входящие значения и записывает результат (1 или 0)
//   в отдельный канал-индикатор через SetData.
//   Значение самого канала температуры проходит без изменений.
//
// Использование:
//   1. Добавьте этот скрипт в таблицу «Скрипты» проекта Rapid SCADA.
//   2. Создайте канал-индикатор (тип «Вычисляемый») для статуса пилы.
//   3. В формулу входного канала (InFormula) канала ТЕМПЕРАТУРЫ впишите:
//        SawtoothCheck(200)
//      где 200 — номер канала-индикатора, куда запишется результат (1/0).
//
//   Параметры:
//     - Порог скачка: 10 градусов (threshold)
//     - Таймаут сброса: 60 минут (timeoutMinutes)
//     - Задержка старта: 5 минут (startDelayMinutes)
//
//   Примеры:
//     SawtoothCheck(200)              — всё по умолчанию
//     SawtoothCheck(200, 10, 60, 15)  — задержка старта 15 минут
//     SawtoothCheck(200, 15, 30, 10)  — порог 15°, таймаут 30 мин, задержка 10 мин
//
//   Канал температуры продолжает показывать температуру как обычно.
//   Канал-индикатор (200) будет показывать:
//     1 — пилообразный сигнал обнаружен
//     0 — пилообразного сигнала нет

protected Dictionary<int, double> SawPrevValues = new Dictionary<int, double>();
protected Dictionary<int, DateTime> SawLastJumpTimes = new Dictionary<int, DateTime>();
protected Dictionary<int, bool> SawActiveFlags = new Dictionary<int, bool>();
protected Dictionary<int, DateTime> SawStartTimes = new Dictionary<int, DateTime>();

public double SawtoothCheck(int outputCnlNum, double threshold = 10.0, double timeoutMinutes = 60.0, double startDelayMinutes = 5.0)
{
    if (!IsCurrent)
        return CnlVal;

    if (CnlStat <= 0)
        return CnlVal;

    double currentValue = CnlVal;
    DateTime now = Timestamp;
    int srcCnl = CnlNum;

    if (!SawStartTimes.ContainsKey(srcCnl))
        SawStartTimes[srcCnl] = now;

    bool isWarming = (now - SawStartTimes[srcCnl]).TotalMinutes < startDelayMinutes;

    SawPrevValues.TryGetValue(srcCnl, out double prevValue);
    bool hasPrev = SawPrevValues.ContainsKey(srcCnl);
    SawPrevValues[srcCnl] = currentValue;

    if (isWarming)
    {
        SawActiveFlags[srcCnl] = false;
        SetData(outputCnlNum, 0.0, 1);
        return CnlVal;
    }

    bool isActive = SawActiveFlags.ContainsKey(srcCnl) && SawActiveFlags[srcCnl];

    if (hasPrev)
    {
        double delta = Math.Abs(currentValue - prevValue);

        if (delta >= threshold)
        {
            isActive = true;
            SawLastJumpTimes[srcCnl] = now;
        }
    }

    if (isActive && SawLastJumpTimes.TryGetValue(srcCnl, out DateTime lastJumpTime))
    {
        if ((now - lastJumpTime).TotalMinutes >= timeoutMinutes)
            isActive = false;
    }

    SawActiveFlags[srcCnl] = isActive;

    SetData(outputCnlNum, isActive ? 1.0 : 0.0, 1);

    return CnlVal;
}
