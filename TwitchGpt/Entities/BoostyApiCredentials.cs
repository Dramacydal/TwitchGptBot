using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TwitchGpt.Entities;

public class BoostyApiCredentials : INotifyPropertyChanged
{
    private int _id;
    private string _userName;
    private long _userId;
    private string _deviceId;
    private string _accessToken;
    private string _refreshToken;
    private long _expiresAt;

    public int Id
    {
        get => _id;
        set => _id = value;
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (_userName == value)
                return;
            
            _userName = value;
            NotifyPropertyChanged();
        }
    }

    public long UserId
    {
        get => _userId;
        set
        {
            if (_userId == value)
                return;
            
            _userId = value;
            NotifyPropertyChanged();
        }
    }

    public string DeviceId
    {
        get => _deviceId;
        set
        {
            if (_deviceId == value)
                return;
            
            _deviceId = value;
            NotifyPropertyChanged();
        }
    }

    public string AccessToken
    {
        get => _accessToken;
        set
        {
            if (_accessToken == value)
                return;
            
            _accessToken = value;
            NotifyPropertyChanged();
        }
    }

    public string RefreshToken
    {
        get => _refreshToken;
        set
        {
            if (_refreshToken == value)
                return;
            
            _refreshToken = value;
            NotifyPropertyChanged();
        }
    }

    public long ExpiresAt
    {
        get => _expiresAt;
        set
        {
            if (_expiresAt == value)
                return;
            
            _expiresAt = value;
            NotifyPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
