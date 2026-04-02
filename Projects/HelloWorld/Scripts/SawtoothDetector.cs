// Sawtooth Detector — скрипт для Rapid SCADA v6
// Детектор пилообразного сигнала.
//
// Назначение:
//   Отслеживает канал на наличие пилообразного сигнала (резких скачков значения
//   на 10 и более градусов). Если скачок обнаружен — статус пилы включается (1).
//   Если в течение 1 часа наблюдения скачков нет — статус пилы выключается (0).
//
// Использование:
//   1. Добавьте этот скрипт в таблицу «Скрипты» проекта Rapid SCADA.
//   2. Создайте расчётный канал (тип «Вычисляемый») для статуса пилы.
//   3. В формулу входного канала (InFormula) статуса пилы впишите:
//        DetectSawtooth(N)
//      где N — номер отслеживаемого канала с температурой.
//
//   Параметры по умолчанию:
//     - Порог скачка: 10 градусов (можно изменить через threshold)
//     - Таймаут сброса: 1 час (можно изменить через timeoutMinutes)
//
//   Пример формулы с нестандартными параметрами:
//        DetectSawtooth(N, 15, 30)
//      — порог 15 градусов, таймаут 30 минут.

// Словарь: номер отслеживаемого канала → предыдущее значение.
protected Dictionary<int, double> SawPrevValues = new Dictionary<int, double>();

// Словарь: номер отслеживаемого канала → время последнего обнаруженного скачка (UTC).
protected Dictionary<int, DateTime> SawLastJumpTimes = new Dictionary<int, DateTime>();

// Словарь: номер отслеживаемого канала → текущий статус пилы (true/false).
protected Dictionary<int, bool> SawActiveFlags = new Dictionary<int, bool>();

/// <summary>
/// Детектирует пилообразный сигнал на канале sourceCnlNum.
/// Возвращает CnlData: значение 1.0 — пила обнаружена, 0.0 — пила не обнаружена.
/// </summary>
/// <param name="sourceCnlNum">Номер канала-источника (температура).</param>
/// <param name="threshold">Порог скачка в градусах (по умолчанию 10).</param>
/// <param name="timeoutMinutes">Время в минутах без скачков для сброса статуса (по умолчанию 60).</param>
public CnlData DetectSawtooth(int sourceCnlNum, double threshold = 10.0, double timeoutMinutes = 60.0)
{
    // Работаем только с текущими данными, не с архивными.
    if (!IsCurrent)
        return Data();

    CnlData sourceData = Data(sourceCnlNum);

    // Если данные источника не определены — возвращаем текущее состояние.
    if (sourceData.IsUndefined)
        return Data();

    double currentValue = sourceData.Val;
    DateTime now = Timestamp;
    bool isActive = SawActiveFlags.ContainsKey(sourceCnlNum) && SawActiveFlags[sourceCnlNum];

    // Проверяем скачок относительно предыдущего значения.
    if (SawPrevValues.TryGetValue(sourceCnlNum, out double prevValue))
    {
        double delta = Math.Abs(currentValue - prevValue);

        if (delta >= threshold)
        {
            // Обнаружен скачок — включаем статус пилы.
            isActive = true;
            SawLastJumpTimes[sourceCnlNum] = now;
        }
    }

    // Сохраняем текущее значение как предыдущее для следующего вызова.
    SawPrevValues[sourceCnlNum] = currentValue;

    // Проверяем таймаут: если пила была активна, но давно не было скачков — выключаем.
    if (isActive && SawLastJumpTimes.TryGetValue(sourceCnlNum, out DateTime lastJumpTime))
    {
        double minutesSinceLastJump = (now - lastJumpTime).TotalMinutes;

        if (minutesSinceLastJump >= timeoutMinutes)
        {
            isActive = false;
        }
    }

    SawActiveFlags[sourceCnlNum] = isActive;
    return NewData(isActive ? 1.0 : 0.0, CnlStatusID.Defined);
}

/// <summary>
/// Упрощённая версия — возвращает double (1 или 0).
/// Можно использовать в формулах, которые ожидают числовое значение.
/// </summary>
public double DetectSawtoothVal(int sourceCnlNum, double threshold = 10.0, double timeoutMinutes = 60.0)
{
    return DetectSawtooth(sourceCnlNum, threshold, timeoutMinutes).Val;
}
