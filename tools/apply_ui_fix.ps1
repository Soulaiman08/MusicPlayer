$path = "c:\Users\win\Desktop\Soulaiman\src\MusicPlayer\Views\MainWindow.xaml"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)

# Find start line: line with Visibility="{Binding ShowEqualizer
$startEq = -1
for ($i = 4600; $i -lt $lines.Length; $i++) {
    if ($lines[$i].Contains("ShowEqualizer") -and $lines[$i].Contains("Grid Row=")) {
        $startEq = $i
        break
    }
}

# Find end line: line with "<!-- UP NEXT / QUEUE CENTER MODAL -->"
$endFx = -1
for ($i = $startEq; $i -lt $lines.Length; $i++) {
    if ($lines[$i].Contains("<!-- UP NEXT / QUEUE CENTER MODAL -->")) {
        $endFx = $i
        break
    }
}

Write-Output "startEq: $($startEq + 1), endFx: $($endFx + 1)"

if ($startEq -ge 0 -and $endFx -gt $startEq) {
    $newSection = @'
        <Grid Row="0" RowSpan="3" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Visibility="{Binding ShowEqualizer, Converter={StaticResource BoolToVisibilityConverter}}">
          <Border Background="#B3000000" MouseLeftButtonDown="ModalBackdrop_Click" />
          <Border HorizontalAlignment="Center" VerticalAlignment="Center" Width="760" Background="#FF1A1D24" BorderBrush="#33FFFFFF" BorderThickness="1" CornerRadius="16" Padding="24">
            <Grid>
              <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
              </Grid.RowDefinitions>
              <Grid Row="0" Margin="0,0,0,16">
                <TextBlock Text="10-BAND EQUALIZER" Style="{StaticResource APK_TextSectionTitle}" VerticalAlignment="Center" />
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                  <TextBlock Text="Enable EQ" Style="{StaticResource APK_TextBodyStyle}" VerticalAlignment="Center" Margin="0,0,8,0" />
                  <CheckBox VerticalAlignment="Center" Margin="0,0,16,0" IsChecked="{Binding EqEnabled, Mode=TwoWay}" />
                  <Button Style="{StaticResource APK_IconButton}" Click="CloseModal_Click">
                    <Path Data="{StaticResource Icon.Close}" Fill="#FFFFFFFF" Stretch="Uniform" Width="14" Height="14" />
                  </Button>
                </StackPanel>
              </Grid>
              <Grid Row="1" Margin="0,0,0,18">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" HorizontalAlignment="Left">
                  <TextBlock Text="Preset:" Style="{StaticResource APK_TextBodyStyle}" VerticalAlignment="Center" Margin="0,0,10,0" />
                  <ComboBox Width="200" DisplayMemberPath="Name" ItemsSource="{Binding EqPresets}" SelectedItem="{Binding CurrentEqPreset, Mode=TwoWay}" />
                </StackPanel>
                <Button HorizontalAlignment="Right" Content="Flat / Reset" Command="{Binding ResetEqualizerCommand}"
                        Background="#26FFFFFF" Foreground="#FFFFFFFF" BorderBrush="#33FFFFFF" BorderThickness="1"
                        Padding="14,6" Cursor="Hand" VerticalAlignment="Center" FontWeight="SemiBold" FontSize="12">
                  <Button.Resources>
                    <Style TargetType="Border">
                      <Setter Property="CornerRadius" Value="8" />
                    </Style>
                  </Button.Resources>
                </Button>
              </Grid>
              <Grid Row="2" Margin="0,10,0,10" IsEnabled="{Binding EqEnabled}">
                <Grid.Style>
                  <Style TargetType="Grid">
                    <Setter Property="Opacity" Value="1.0" />
                    <Style.Triggers>
                      <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.38" />
                      </Trigger>
                    </Style.Triggers>
                  </Style>
                </Grid.Style>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band0, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band0, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="31Hz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="1" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band1, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band1, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="62Hz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="2" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band2, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band2, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="125Hz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="3" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band3, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band3, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="250Hz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="4" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band4, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band4, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="500Hz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="5" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band5, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band5, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="1kHz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="6" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band6, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band6, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="2kHz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="7" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band7, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band7, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="4kHz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="8" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band8, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band8, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="8kHz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
                <StackPanel Grid.Column="9" HorizontalAlignment="Center">
                  <TextBlock Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,0,0,4" Text="{Binding Band9, StringFormat={}{0:+0;-0;0} dB}" />
                  <Grid Height="140" Width="14">
                    <Border Height="2" Width="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                    <Slider Orientation="Vertical" Height="140" Minimum="-15" Maximum="15" Style="{StaticResource APK_SeekSlider_Vertical}" Cursor="Hand" Value="{Binding Band9, Mode=TwoWay}" />
                  </Grid>
                  <TextBlock Text="16kHz" Style="{StaticResource APK_TextLowStyle}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                </StackPanel>
              </Grid>
            </Grid>
          </Border>
        </Grid>
        <Grid Row="0" RowSpan="3" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Visibility="{Binding ShowSoundEffects, Converter={StaticResource BoolToVisibilityConverter}}">
          <Border Background="#B3000000" MouseLeftButtonDown="ModalBackdrop_Click" />
          <Border HorizontalAlignment="Center" VerticalAlignment="Center" Width="700" Background="#FF1A1D24" BorderBrush="#33FFFFFF" BorderThickness="1" CornerRadius="16" Padding="24">
            <StackPanel>
              <Grid Margin="0,0,0,16">
                <TextBlock Text="SOUND EFFECTS &amp; ACOUSTIC CONTROLS" Style="{StaticResource APK_TextSectionTitle}" VerticalAlignment="Center" />
                <Button Style="{StaticResource APK_IconButton}" HorizontalAlignment="Right" Click="CloseModal_Click">
                  <Path Data="{StaticResource Icon.Close}" Fill="#FFFFFFFF" Stretch="Uniform" Width="14" Height="14" />
                </Button>
              </Grid>
              <UniformGrid Columns="3" Rows="2" Margin="0,0,0,12">
                <Border Margin="6" Padding="16,12" CornerRadius="12" Cursor="Hand" BorderThickness="1.5">
                  <FrameworkElement.Style>
                    <Style TargetType="{x:Type Border}">
                      <Setter Property="Background" Value="#1AFFFFFF" />
                      <Setter Property="BorderBrush" Value="#22FFFFFF" />
                      <Style.Triggers>
                        <DataTrigger Value="0" Binding="{Binding CurrentSoundEffect}">
                          <Setter Property="Background" Value="#333B82F6" />
                          <Setter Property="BorderBrush" Value="#FF3B82F6" />
                        </DataTrigger>
                      </Style.Triggers>
                    </Style>
                  </FrameworkElement.Style>
                  <UIElement.InputBindings>
                    <MouseBinding MouseAction="LeftClick" CommandParameter="0" Command="{Binding ToggleSoundEffectCommand}" />
                  </UIElement.InputBindings>
                  <StackPanel IsHitTestVisible="False" VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock Text="Electronic Tube" FontSize="15" FontWeight="SemiBold" Foreground="#FFFFFFFF" HorizontalAlignment="Center" />
                    <TextBlock Text="Harmonic saturation" FontSize="11" Foreground="#99FFFFFF" HorizontalAlignment="Center" Margin="0,4,0,0" />
                  </StackPanel>
                </Border>
                <Border Margin="6" Padding="16,12" CornerRadius="12" Cursor="Hand" BorderThickness="1.5">
                  <FrameworkElement.Style>
                    <Style TargetType="{x:Type Border}">
                      <Setter Property="Background" Value="#1AFFFFFF" />
                      <Setter Property="BorderBrush" Value="#22FFFFFF" />
                      <Style.Triggers>
                        <DataTrigger Value="1" Binding="{Binding CurrentSoundEffect}">
                          <Setter Property="Background" Value="#333B82F6" />
                          <Setter Property="BorderBrush" Value="#FF3B82F6" />
                        </DataTrigger>
                      </Style.Triggers>
                    </Style>
                  </FrameworkElement.Style>
                  <UIElement.InputBindings>
                    <MouseBinding MouseAction="LeftClick" CommandParameter="1" Command="{Binding ToggleSoundEffectCommand}" />
                  </UIElement.InputBindings>
                  <StackPanel IsHitTestVisible="False" VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock Text="3D Rotate" FontSize="15" FontWeight="SemiBold" Foreground="#FFFFFFFF" HorizontalAlignment="Center" />
                    <TextBlock Text="Spatial rotation" FontSize="11" Foreground="#99FFFFFF" HorizontalAlignment="Center" Margin="0,4,0,0" />
                  </StackPanel>
                </Border>
                <Border Margin="6" Padding="16,12" CornerRadius="12" Cursor="Hand" BorderThickness="1.5">
                  <FrameworkElement.Style>
                    <Style TargetType="{x:Type Border}">
                      <Setter Property="Background" Value="#1AFFFFFF" />
                      <Setter Property="BorderBrush" Value="#22FFFFFF" />
                      <Style.Triggers>
                        <DataTrigger Value="2" Binding="{Binding CurrentSoundEffect}">
                          <Setter Property="Background" Value="#333B82F6" />
                          <Setter Property="BorderBrush" Value="#FF3B82F6" />
                        </DataTrigger>
                      </Style.Triggers>
                    </Style>
                  </FrameworkElement.Style>
                  <UIElement.InputBindings>
                    <MouseBinding MouseAction="LeftClick" CommandParameter="2" Command="{Binding ToggleSoundEffectCommand}" />
                  </UIElement.InputBindings>
                  <StackPanel IsHitTestVisible="False" VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock Text="Tone Low" FontSize="15" FontWeight="SemiBold" Foreground="#FFFFFFFF" HorizontalAlignment="Center" />
                    <TextBlock Text="Car Bass &amp; Chamber Reverb" FontSize="11" Foreground="#99FFFFFF" HorizontalAlignment="Center" Margin="0,4,0,0" />
                  </StackPanel>
                </Border>
                <Border Margin="6" Padding="16,12" CornerRadius="12" Cursor="Hand" BorderThickness="1.5">
                  <FrameworkElement.Style>
                    <Style TargetType="{x:Type Border}">
                      <Setter Property="Background" Value="#1AFFFFFF" />
                      <Setter Property="BorderBrush" Value="#22FFFFFF" />
                      <Style.Triggers>
                        <DataTrigger Value="3" Binding="{Binding CurrentSoundEffect}">
                          <Setter Property="Background" Value="#333B82F6" />
                          <Setter Property="BorderBrush" Value="#FF3B82F6" />
                        </DataTrigger>
                      </Style.Triggers>
                    </Style>
                  </FrameworkElement.Style>
                  <UIElement.InputBindings>
                    <MouseBinding MouseAction="LeftClick" CommandParameter="3" Command="{Binding ToggleSoundEffectCommand}" />
                  </UIElement.InputBindings>
                  <StackPanel IsHitTestVisible="False" VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock Text="Surround" FontSize="15" FontWeight="SemiBold" Foreground="#FFFFFFFF" HorizontalAlignment="Center" />
                    <TextBlock Text="Stereo field widening" FontSize="11" Foreground="#99FFFFFF" HorizontalAlignment="Center" Margin="0,4,0,0" />
                  </StackPanel>
                </Border>
                <Border Margin="6" Padding="16,12" CornerRadius="12" Cursor="Hand" BorderThickness="1.5">
                  <FrameworkElement.Style>
                    <Style TargetType="{x:Type Border}">
                      <Setter Property="Background" Value="#1AFFFFFF" />
                      <Setter Property="BorderBrush" Value="#22FFFFFF" />
                      <Style.Triggers>
                        <DataTrigger Value="4" Binding="{Binding CurrentSoundEffect}">
                          <Setter Property="Background" Value="#333B82F6" />
                          <Setter Property="BorderBrush" Value="#FF3B82F6" />
                        </DataTrigger>
                      </Style.Triggers>
                    </Style>
                  </FrameworkElement.Style>
                  <UIElement.InputBindings>
                    <MouseBinding MouseAction="LeftClick" CommandParameter="4" Command="{Binding ToggleSoundEffectCommand}" />
                  </UIElement.InputBindings>
                  <StackPanel IsHitTestVisible="False" VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock Text="Magic Sound" FontSize="15" FontWeight="SemiBold" Foreground="#FFFFFFFF" HorizontalAlignment="Center" />
                    <TextBlock Text="Flanger &amp; Sparkle Shimmer" FontSize="11" Foreground="#99FFFFFF" HorizontalAlignment="Center" Margin="0,4,0,0" />
                  </StackPanel>
                </Border>
                <Border Margin="6" Padding="16,12" CornerRadius="12" Cursor="Hand" BorderThickness="1.5">
                  <FrameworkElement.Style>
                    <Style TargetType="{x:Type Border}">
                      <Setter Property="Background" Value="#1AFFFFFF" />
                      <Setter Property="BorderBrush" Value="#22FFFFFF" />
                      <Style.Triggers>
                        <DataTrigger Value="5" Binding="{Binding CurrentSoundEffect}">
                          <Setter Property="Background" Value="#333B82F6" />
                          <Setter Property="BorderBrush" Value="#FF3B82F6" />
                        </DataTrigger>
                      </Style.Triggers>
                    </Style>
                  </FrameworkElement.Style>
                  <UIElement.InputBindings>
                    <MouseBinding MouseAction="LeftClick" CommandParameter="5" Command="{Binding ToggleSoundEffectCommand}" />
                  </UIElement.InputBindings>
                  <StackPanel IsHitTestVisible="False" VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock Text="Live Treble" FontSize="15" FontWeight="SemiBold" Foreground="#FFFFFFFF" HorizontalAlignment="Center" />
                    <TextBlock Text="High shelf boost" FontSize="11" Foreground="#99FFFFFF" HorizontalAlignment="Center" Margin="0,4,0,0" />
                  </StackPanel>
                </Border>
              </UniformGrid>

              <!-- Acoustic Enhancements (Decoupled from EQ) -->
              <Border Height="1" Background="#26FFFFFF" Margin="0,12,0,14" />
              <TextBlock Text="ACOUSTIC ENHANCEMENTS" FontSize="12" FontWeight="SemiBold" Foreground="#B3FFFFFF" Margin="6,0,0,10" />

              <Grid Margin="0,0,0,10">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Margin="4" Padding="12" Background="#14FFFFFF" CornerRadius="10" BorderBrush="#1FFFFFFF" BorderThickness="1">
                  <StackPanel>
                    <DockPanel Margin="0,0,0,6">
                      <TextBlock Text="Bass Boost" Style="{StaticResource APK_TextBodyStyle}" FontWeight="SemiBold" />
                      <CheckBox HorizontalAlignment="Right" IsChecked="{Binding BassBoostEnabled, Mode=TwoWay}" />
                    </DockPanel>
                    <Slider Minimum="0" Maximum="1" Style="{StaticResource APK_SeekSlider}" Height="14" Cursor="Hand" Value="{Binding BassBoostStrength, Mode=TwoWay}" />
                  </StackPanel>
                </Border>
                <Border Grid.Column="1" Margin="4" Padding="12" Background="#14FFFFFF" CornerRadius="10" BorderBrush="#1FFFFFFF" BorderThickness="1">
                  <StackPanel>
                    <DockPanel Margin="0,0,0,6">
                      <TextBlock Text="3D Virtualizer" Style="{StaticResource APK_TextBodyStyle}" FontWeight="SemiBold" />
                      <CheckBox HorizontalAlignment="Right" IsChecked="{Binding VirtualizerEnabled, Mode=TwoWay}" />
                    </DockPanel>
                    <Slider Minimum="0" Maximum="1" Style="{StaticResource APK_SeekSlider}" Height="14" Cursor="Hand" Value="{Binding VirtualizerStrength, Mode=TwoWay}" />
                  </StackPanel>
                </Border>
                <Border Grid.Column="2" Margin="4" Padding="12" Background="#14FFFFFF" CornerRadius="10" BorderBrush="#1FFFFFFF" BorderThickness="1">
                  <StackPanel>
                    <TextBlock Text="L/R Balance" Style="{StaticResource APK_TextBodyStyle}" FontWeight="SemiBold" Margin="0,0,0,6" />
                    <Grid>
                      <Border Width="2" Height="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                      <Slider Minimum="-1" Maximum="1" Style="{StaticResource APK_SeekSlider}" Height="14" Cursor="Hand" Value="{Binding Balance, Mode=TwoWay}" />
                    </Grid>
                  </StackPanel>
                </Border>
              </Grid>

              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Margin="4" Padding="12" Background="#14FFFFFF" CornerRadius="10" BorderBrush="#1FFFFFFF" BorderThickness="1">
                  <StackPanel>
                    <DockPanel Margin="0,0,0,8">
                      <TextBlock Text="Reverb" Style="{StaticResource APK_TextBodyStyle}" FontWeight="SemiBold" VerticalAlignment="Center" />
                      <CheckBox Content="Enable" Margin="8,0,0,0" VerticalAlignment="Center" IsChecked="{Binding ReverbEnabled, Mode=TwoWay}" />
                      <ComboBox Width="130" HorizontalAlignment="Right" SelectedIndex="{Binding ReverbPreset, Mode=TwoWay}">
                        <ComboBoxItem Content="None" />
                        <ComboBoxItem Content="Small Room" />
                        <ComboBoxItem Content="Medium Room" />
                        <ComboBoxItem Content="Large Room" />
                        <ComboBoxItem Content="Medium Hall" />
                        <ComboBoxItem Content="Large Hall" />
                        <ComboBoxItem Content="Plate" />
                      </ComboBox>
                    </DockPanel>
                  </StackPanel>
                </Border>
                <Border Grid.Column="1" Margin="4" Padding="12" Background="#14FFFFFF" CornerRadius="10" BorderBrush="#1FFFFFFF" BorderThickness="1">
                  <StackPanel>
                    <DockPanel Margin="0,0,0,6">
                      <TextBlock Text="Loudness" Style="{StaticResource APK_TextBodyStyle}" FontWeight="SemiBold" VerticalAlignment="Center" />
                      <TextBlock Style="{StaticResource APK_TextLowStyle}" Margin="8,0,0,0" VerticalAlignment="Center" Text="{Binding LoudnessGain, StringFormat={}{0:+0;-0;0} dB}" />
                      <CheckBox HorizontalAlignment="Right" IsChecked="{Binding LoudnessEnabled, Mode=TwoWay}" />
                    </DockPanel>
                    <Grid>
                      <Border Width="2" Height="10" Background="#40FFFFFF" CornerRadius="1" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
                      <Slider Minimum="-12" Maximum="12" Style="{StaticResource APK_SeekSlider}" Height="14" Cursor="Hand" Value="{Binding LoudnessGain, Mode=TwoWay}" />
                    </Grid>
                  </StackPanel>
                </Border>
              </Grid>
            </StackPanel>
          </Border>
        </Grid>
'@

    $before = $lines[0..($startEq - 1)]
    $after = $lines[$endFx..($lines.Length - 1)]
    $combined = @()
    $combined += $before
    $combined += $newSection
    $combined += $after
    
    [System.IO.File]::WriteAllLines($path, $combined, [System.Text.Encoding]::UTF8)
    Write-Output "Successfully updated MainWindow.xaml"
} else {
    Write-Error "Markers not found: startEq=$startEq, endFx=$endFx"
}
