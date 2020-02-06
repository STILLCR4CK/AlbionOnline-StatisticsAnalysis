﻿namespace StatisticsAnalysisTool.Views
{
    using Common;
    using Models;
    using Properties;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Input;
    using ViewModels;

    /// <summary>
    ///     Interaktionslogik für ItemWindow.xaml
    /// </summary>
    public partial class ItemWindow
    {

        private readonly ItemWindowViewModel _itemWindowViewModel;

        private ItemData _itemData =  new ItemData();
        private string _uniqueName;
        private bool _runUpdate = true;
        private bool _isAutoUpdateActive;

        public ItemWindow(Item item)
        {
            InitializeComponent();
            _itemWindowViewModel = new ItemWindowViewModel(this);
            DataContext = _itemWindowViewModel;

            LblItemName.Content = "";
            LblItemId.Content = "";
            LblLastUpdate.Content = "";

            InitializeItemData(item);

            ListViewPrices.Language = System.Windows.Markup.XmlLanguage.GetLanguage(LanguageController.DefaultCultureInfo.ToString());
        }
        
        private async void InitializeItemData(Item item)
        {
            if (item == null)
                return;

            _uniqueName = item.UniqueName;

            if (Dispatcher == null)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                FaLoadIcon.Visibility = Visibility.Visible;
                Icon = item.Icon;
            });

            StartAutoUpdater();

            var itemDataTaskResult = await ApiController.GetItemDataFromJsonAsync(item);

            if (itemDataTaskResult == null)
            {
                LblItemName.Content = StatisticsAnalysisManager.LanguageController.Translation("ERROR_PRICES_CAN_NOT_BE_LOADED");
                Dispatcher?.Invoke(() => { FaLoadIcon.Visibility = Visibility.Hidden; });
                return;
            }

            _itemData = itemDataTaskResult;

            if (Dispatcher == null)
                return;

            await Dispatcher.InvokeAsync(() =>
                {
                    Title = $"{_itemData.LocalizedName} (T{_itemData.Tier})";
                    LblItemName.Content = $"{_itemData.LocalizedName} (T{_itemData.Tier})";
                    LblItemId.Content = _itemData.UniqueName;
                    ImgItemImage.Source = item.Icon;

                    FaLoadIcon.Visibility = Visibility.Hidden;
                });
        
        }

        private async void StartAutoUpdater()
        {
            await Task.Run(async () => {
                if (_isAutoUpdateActive)
                    return;

                _isAutoUpdateActive = true;
                while (_runUpdate)
                {
                    await Task.Delay(500);
                    if (Dispatcher != null && Dispatcher.Invoke(() => !ChbAutoUpdateData.IsChecked ?? false))
                        continue;

                    GetPriceStats(_uniqueName);
                    await Task.Delay(Settings.Default.RefreshRate - 500);
                }
                _isAutoUpdateActive = false;
            });
        }
        
        private async void GetPriceStats(string uniqueName)
        {
            if (uniqueName == null)
                return;

            await Task.Run(async () =>
            {
                var showVillagesIsChecked = Dispatcher != null && Dispatcher.Invoke(() => ChbShowVillages.IsChecked ?? false);
                var showBlackZoneOutpostsIsChecked = Dispatcher != null && Dispatcher.Invoke(() => ChbShowBlackZoneOutposts.IsChecked ?? false);

                var statPricesList = await ApiController.GetCityItemPricesFromJsonAsync(uniqueName, Locations.GetLocationsListByArea(new IsLocationAreaActive()
                {
                    Cities = true,
                    Villages = showVillagesIsChecked,
                    BlackZoneOutposts = showBlackZoneOutpostsIsChecked
                }));
                
                if (statPricesList == null)
                    return;

                var statsPricesTotalList = PriceUpdate(statPricesList);

                FindBestPrice(ref statsPricesTotalList);

                var marketCurrentPricesItemList = new List<MarketCurrentPricesItem>();
                foreach (var item in statsPricesTotalList)
                    marketCurrentPricesItemList.Add(new MarketCurrentPricesItem(item));

                Dispatcher?.Invoke(() =>
                {
                    ListViewPrices.ItemsSource = marketCurrentPricesItemList;
                    SetDifferenceCalculationText(statsPricesTotalList);
                    LblLastUpdate.Content = Utilities.DateFormat(DateTime.Now, 0);
                });
            });
        }

        private List<MarketResponseTotal> PriceUpdate(List<MarketResponse> statPricesList)
        {
            var statsPricesTotalList = new List<MarketResponseTotal>();

            foreach (var stats in statPricesList)
            {
                if (statsPricesTotalList.Exists(s => Locations.GetParameterName(s.City) == stats.City))
                {
                    var spt = statsPricesTotalList.Find(s => Locations.GetName(s.City) == stats.City);
                    if (stats.SellPriceMin < spt.SellPriceMin)
                        spt.SellPriceMin = stats.SellPriceMin;

                    if (stats.SellPriceMax > spt.SellPriceMax)
                        spt.SellPriceMax = stats.SellPriceMax;

                    if (stats.BuyPriceMin < spt.BuyPriceMin)
                        spt.BuyPriceMin = stats.BuyPriceMin;

                    if (stats.BuyPriceMax > spt.BuyPriceMax)
                        spt.BuyPriceMax = stats.BuyPriceMax;
                }
                else
                {
                    statsPricesTotalList.Add(new MarketResponseTotal(stats));
                }
            }

            return statsPricesTotalList;
        }
        
        private void FindBestPrice(ref List<MarketResponseTotal> list)
        {
            if (list.Count == 0)
                return;

            var max = GetMaxPrice(list);

            try
            {
                list.Find(s => s.BuyPriceMax == max).BestBuyMaxPrice = true;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }

            var min = GetMinPrice(list);

            try
            {
                list.Find(s => s.SellPriceMin == min).BestSellMinPrice = true;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }

        }

        private static ulong GetMaxPrice(List<MarketResponseTotal> list)
        {
            var max = ulong.MinValue;
            foreach (var type in list)
            {
                if (type.BuyPriceMax == 0)
                    continue;

                if (type.BuyPriceMax > max)
                    max = type.BuyPriceMax;
            }

            return max;
        }

        private static ulong GetMinPrice(List<MarketResponseTotal> list)
        {
            var min = ulong.MaxValue;
            foreach (var type in list)
            {
                if (type.SellPriceMin == 0)
                    continue;

                if (type.SellPriceMin < min)
                    min = type.SellPriceMin;
            }

            return min;
        }

        private void SetDifferenceCalculationText(List<MarketResponseTotal> statsPricesTotalList)
        {
            ulong? bestBuyMaxPrice = 0UL;
            ulong? bestSellMinPrice = 0UL;

            if (statsPricesTotalList?.Count > 0)
            {
                bestBuyMaxPrice = statsPricesTotalList.FirstOrDefault(s => s.BestBuyMaxPrice)?.BuyPriceMax ?? 0UL;
                bestSellMinPrice = statsPricesTotalList.FirstOrDefault(s => s.BestSellMinPrice)?.SellPriceMin ?? 0UL;
            }

            var diffPrice = (int)bestBuyMaxPrice - (int)bestSellMinPrice;

            LblDifCalcText.Content = $"{StatisticsAnalysisManager.LanguageController.Translation("BOUGHT_FOR")} {string.Format(LanguageController.DefaultCultureInfo, "{0:n0}", bestSellMinPrice)} | " +
                                     $"{StatisticsAnalysisManager.LanguageController.Translation("SELL_FOR")} {string.Format(LanguageController.DefaultCultureInfo, "{0:n0}", bestBuyMaxPrice)} | " +
                                     $"{StatisticsAnalysisManager.LanguageController.Translation("PROFIT")} {string.Format(LanguageController.DefaultCultureInfo, "{0:n0}", diffPrice)}";
        }
        
        private void Hotbar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _runUpdate = false;
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void ShowVillagesPrices_Click(object sender, RoutedEventArgs e) => GetPriceStats(_uniqueName);

        private void ChbShowBlackZoneOutposts_Click(object sender, RoutedEventArgs e) => GetPriceStats(_uniqueName);

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && WindowState == WindowState.Normal)
            {
                WindowState = WindowState.Maximized;
                return;
            }

            if (e.ClickCount == 2 && WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        }
    }
}