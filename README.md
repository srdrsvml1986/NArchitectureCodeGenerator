# nArchGen CLI

nArchGen CLI, nArchitecture tabanlı .NET projelerini hızlı bir şekilde oluşturmanızı ve yönetmenizi sağlayan bir komut satırı aracıdır.

## Özellikler

- 🏗️ Yeni proje oluşturma
- 🔄 CRUD operasyonları oluşturma 
- 📝 Özel komutlar oluşturma
- 🔍 Sorgu (Query) operasyonları oluşturma

## Kurulum
## Kullanım

### Yeni Proje Oluşturma
### CRUD Operasyonları Oluşturma
### Özel Komut Oluşturma
### Sorgu (Query) Oluşturma
## Proje Yapısı

- **Core Paketleri**: Code generation, dosya işlemleri ve çapraz kesişen konular için temel paketler
- **Domain**: İş mantığı ve proje sabitleri
- **Application**: Komut ve sorgu işleyicileri 
- **ConsoleUI**: CLI arayüzü ve komut yapılandırması

## Mekanizmalar

- **Önbellekleme (Caching)**
- **Loglama (Logging)** 
- **Transaction Yönetimi**
- **Güvenlik Operasyonları**

## Güvenlik Seçenekleri

- JWT token tabanlı kimlik doğrulama
- Role/izin tabanlı yetkilendirme 
- Güvenli Swagger arayüzü

## Geliştirici Notları

- `Application/Features` altında özellik bazlı klasörleme yapısı
- Scriban template motoru ile kod üretimi
- Git entegrasyonu ile otomatik repo oluşturma
- Azure DevOps pipeline dosyaları
