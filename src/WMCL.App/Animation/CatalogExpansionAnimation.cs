namespace WMCL.App.Animation;

internal sealed class CatalogExpansionAnimation
{
    private readonly double _startHeight;
    private readonly double _targetHeight;
    private readonly double _distance;
    private readonly double _easeDistance;
    private readonly double _travelMilliseconds;
    private readonly double _settleMilliseconds;
    private readonly double _initialSpeed;

    public CatalogExpansionAnimation(double startHeight, double targetHeight)
    {
        _startHeight = startHeight;
        _targetHeight = targetHeight;
        _distance = Math.Abs(targetHeight - startHeight);
        _easeDistance = _distance;
        _settleMilliseconds = 150;
        if (_distance <= 800) return;

        if (targetHeight < startHeight)
        {
            _easeDistance = 200;
            _settleMilliseconds = 150;
            _initialSpeed = (_distance - _easeDistance) / 0.1;
        }
        else if (_distance > 3000)
        {
            _initialSpeed = 5000;
            _easeDistance = _distance - _initialSpeed * 0.3;
            _settleMilliseconds = 400;
        }
        else
        {
            _easeDistance = 150;
            _settleMilliseconds = 200;
            _initialSpeed = 4000;
        }
        _travelMilliseconds = (_distance - _easeDistance) / _initialSpeed * 1000;
    }

    public double DurationMilliseconds => _travelMilliseconds + _settleMilliseconds;

    public double HeightAt(double milliseconds)
    {
        if (milliseconds <= 0) return _startHeight;
        if (milliseconds >= DurationMilliseconds) return _targetHeight;
        var traveled = milliseconds < _travelMilliseconds
            ? _initialSpeed * milliseconds / 1000
            : _distance - _easeDistance + _easeDistance * Settle(
                (milliseconds - _travelMilliseconds) / _settleMilliseconds);
        return _startHeight + Math.Sign(_targetHeight - _startHeight) * traveled;
    }

    private double Settle(double progress)
    {
        if (_initialSpeed == 0) return EaseOut(progress);
        var alpha = Math.Max(0, _initialSpeed * _settleMilliseconds / 1000 / _easeDistance - 1);
        return (alpha + 1) * progress / (1 + alpha * progress);
    }

    public static double EaseOut(double progress) => 1 - Math.Pow(1 - Math.Clamp(progress, 0, 1), 5);
}
